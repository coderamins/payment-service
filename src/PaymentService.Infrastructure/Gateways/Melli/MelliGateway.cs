using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Common;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Gateways.Sedad;

public class MelliGateway : IPaymentGateway
{
    private const short Success = 0;
    private const short AlreadyVerified = 10;    
    private const short TimeOver = 202;

    private readonly MelliApiClient _api;
    private readonly MelliOptions _options;
    private readonly ILogger<MelliGateway> _logger;

    public GatewayProvider Provider => GatewayProvider.Sadad;

    public MelliGateway(MelliApiClient api, MelliOptions options,
        ILogger<MelliGateway> logger)
    {
        _api = api;
        _options = options;
        _logger = logger;
    }

    public async Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction, string callbackUrl,
        string? payerNationalCode = null, CancellationToken cancellationToken = default)
    {
        var res = await _api.PaymentRequestAsync(
             transaction.PaymentTransactionId,
             transaction.AmountInRials,
             callbackUrl,
             ResolveSplits(transaction),
             cancellationToken);

        if (res is null)
            return new GatewayRequestResult(false, null, null, "پاسخ نامعتبر از پرداخت سداد");

        if (res.ResCode != Success || string.IsNullOrWhiteSpace(res.Token))
            return new GatewayRequestResult(false, null, null, MapError(res.ResCode, res.Description));

        var redirectUrl = $"{_options.PaymentPageUrl.TrimEnd('/')}/{res.Token}";
        return new GatewayRequestResult(true, res.Token, redirectUrl, null);   // GET
    }

    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> raw)
    {
        var resCodeRaw = Get(raw, "ResCode");
        var token = Get(raw, "Token");
        var maskedCard = Get(raw, "PrimaryAccNo");

        raw.TryGetValue("Amount", out var amountRaw);

        long? reportedAmount = long.TryParse(amountRaw, out var amount) ? amount : null;

        if (!short.TryParse(resCodeRaw, out var resCode)) resCode = -999;

        var paid = resCode == Success;

        return new GatewayCallbackParseResult(
            paid,
            resCode.ToString(),
            token,                    
            maskedCard,
            paid ? null : "پرداخت ناموفق بود یا توسط کاربر لغو شد",
            reportedAmount,
            token
            );
    }

    /// <summary> در ملت وریفای همان تسویه هستش و برای اینکه کنترل شده تسویه انجام دهیم اینجا هیچ کاری نمیکنیم </summary>
    public Task<GatewayOperationResult> VerifyAsync(
           PaymentTransaction transaction, CancellationToken cancellationToken = default)
           => Task.FromResult(new GatewayOperationResult(true, "0", null));

    public async Task<GatewayOperationResult> SettleAsync(
       PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewayRefId))
            return new GatewayOperationResult(false, "NO_TOKEN", "توکن تراکنش یافت نشد");

        var res = await _api.VerifyAsync(transaction.GatewayRefId, cancellationToken);
        if (res is null)
            return new GatewayOperationResult(false, "NO_RESPONSE", "پاسخ نامعتبر از سداد");

        if (res.ResCode is not (Success or AlreadyVerified))
            return new GatewayOperationResult(false, res.ResCode.ToString(),
                MapVerifyError(res.ResCode, res.Description));

        if (res.Amount != transaction.AmountInRials)
        {
            _logger.LogCritical(
                "مغایرت مبلغ در Verify سداد: تراکنش {Id} ثبت‌شده {Expected} بانک {Reported}",
                transaction.PaymentTransactionId, transaction.AmountInRials, res.Amount);

            try { await _api.ReverseAsync(transaction.GatewayRefId, transaction.AmountInRials, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Reverse بعد از مغایرت مبلغ ناموفق بود: {Id}", transaction.PaymentTransactionId); }

            return new GatewayOperationResult(false, "AMOUNT_MISMATCH",
                "مبلغ پرداخت‌شده با مبلغ سفارش مغایرت دارد");
        }

        return new GatewayOperationResult(true, res.ResCode.ToString(), null);
    }

    public async Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewayRefId))
            return new GatewayOperationResult(false, "NO_TOKEN", "توکن تراکنش یافت نشد");

        var res = await _api.ReverseAsync(
            transaction.GatewayRefId, transaction.AmountInRials, cancellationToken);
        if (res is null)
            return new GatewayOperationResult(false, "NO_RESPONSE", "پاسخ نامعتبر از سداد");

        if (res.ResCode == Success)
            return new GatewayOperationResult(true, "0", null);

        // تراکنش تأییدنشده بعد از پایان مهلت خودکار برگشت می‌خورد
        if (res.ResCode == TimeOver && transaction.PaymentStatusId != EPaymentStatus.Settled)
            return new GatewayOperationResult(true, "0", null);

        return new GatewayOperationResult(false, res.ResCode.ToString(),
            MapReverseError(res.ResCode, res.Description));
    }

    private IReadOnlyList<PaymentSplitShare> ResolveSplits(PaymentTransaction transaction)
    {
        var splits = transaction.GetSplits();
        if (splits.Count > 0 || string.IsNullOrWhiteSpace(_options.DefaultSheba))
            return splits;

        // ترمینال «تسهیم از سمت پذیرنده»: پرداخت بدون تسهیم هم باید کل مبلغ را روی یک حساب بفرستد
        var sheba = PaymentSplitCalculator.NormalizeSheba(_options.DefaultSheba)
            ?? throw new InvalidOperationException("PaymentGateways:Melli:DefaultSheba نامعتبر است");

        return new[] { new PaymentSplitShare(sheba, (long)transaction.AmountInRials) };
    }

    private static string? Get(IDictionary<string, string> p, string key) =>
        p.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string MapError(int code, string? bankMessage) => code switch
    {
        3 or 23 => "پذیرنده در سداد فعال نیست",
        58 => "انجام تراکنش توسط پایانه مجاز نیست",
        61 => "مبلغ تراکنش از حد مجاز بالاتر است",
        1000 or 1001 => "پارامترهای ارسالی نادرست است",
        1003 or 1029 => "IP سرور پذیرنده در سداد ثبت نشده است",
        1011 => "شماره‌ی سفارش تکراری است",
        1012 or 1025 => "اطلاعات پذیرنده یا امضا نادرست است (کلید/زمان/تاریخ)",
        1017 => "مبلغ از سقف مجاز پذیرنده بیشتر است",
        1018 => "تاریخ و زمان سرور با بانک هماهنگ نیست",
        1023 or 1030 => "آدرس بازگشت یا دامنه در سداد ثبت نشده است",
        1026 => "شماره سفارش نامعتبر است",
        1027 => "شماره پذیرنده نامعتبر است",
        1028 or 1037 => "شماره ترمینال نامعتبر است",
        1056 or 1066 or 1068 => "سیستم موقتاً قطع است؛ بعداً تلاش کنید",
        1058 => "سرویس پرداخت اینترنتی سداد خارج از سرویس است",
        _ => $"خطای شماره {code} از سوی سداد" +
             (string.IsNullOrWhiteSpace(bankMessage) ? "" : $" ({bankMessage})")
    };

    private static string MapVerifyError(int code, string? bankMessage) => code switch
    {
        -1 => "پارامترهای Verify نادرست است یا تراکنشی یافت نشد",
        101 => "مهلت تأیید تراکنش به پایان رسیده است",
        _ => $"خطای Verify شماره {code} از سوی سداد" +
             (string.IsNullOrWhiteSpace(bankMessage) ? "" : $" ({bankMessage})")
    };

    private static string MapReverseError(int code, string? bankMessage) => code switch
    {
        -1 => "پارامترهای برگشت نادرست است یا تراکنشی یافت نشد",
        64 => "درخواست برگشت مجاز نیست",
        202 => "مهلت برگشت وجه به پایان رسیده است",
        _ => $"خطای برگشت شماره {code} از سوی سداد" +
             (string.IsNullOrWhiteSpace(bankMessage) ? "" : $" ({bankMessage})")
    };
}