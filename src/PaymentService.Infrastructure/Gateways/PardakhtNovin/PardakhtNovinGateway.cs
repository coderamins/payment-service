using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Gateways.PardakhtNovin;

public class PardakhtNovinGateway : IPaymentGateway
{
    private const short Success = 0;
    private const short AlreadyConfirmed = 2;     // تأیید قبلاً انجام شده
    private const short CanceledByUser = -138;

    private readonly PardakhtNovinApiClient _api;
    private readonly PardakhtNovinOptions _options;
    private readonly ILogger<PardakhtNovinGateway> _logger;

    public GatewayProvider Provider => GatewayProvider.PardakhtNovin;

    public PardakhtNovinGateway(PardakhtNovinApiClient api, PardakhtNovinOptions options,
        ILogger<PardakhtNovinGateway> logger)
    {
        _api = api;
        _options = options;
        _logger = logger;
    }

    public async Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction, string callbackUrl,
        string? payerNationalCode = null, CancellationToken cancellationToken = default)
    {
        var res = await _api.NormalSaleAsync(
            transaction.PaymentTransactionId, transaction.AmountInRials, callbackUrl, cancellationToken);

        if (res is null)
            return new GatewayRequestResult(false, null, null, "پاسخ نامعتبر از پرداخت نوین");

        if (res.Status != Success || string.IsNullOrWhiteSpace(res.Token))
            return new GatewayRequestResult(false, null, null, MapError(res.Status, res.Message));

        var redirectUrl = $"{_options.PaymentPageUrl.TrimEnd('/')}/{res.Token}";
        return new GatewayRequestResult(true, res.Token, redirectUrl, null);  
    }

    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> raw)
    {
        var statusRaw = Get(raw, "Status");
        var rrn = Get(raw, "RRN");
        var token = Get(raw, "Token");   
        var amountRaw = Get(raw, "Amount");   

        if (!short.TryParse(statusRaw, out var status)) status = -999;

        var paid = status == Success && !string.IsNullOrWhiteSpace(rrn);

        long? reportedAmount = long.TryParse(amountRaw, out var amount) ? amount : null;

        var error = paid ? null
            : status == CanceledByUser ? "پرداخت توسط کاربر لغو شد"
            : MapError(status, null);

        return new GatewayCallbackParseResult(
            paid, 
            status.ToString(), 
            rrn, 
            null, 
            error, 
            reportedAmount,
            token);
    }

    /// <summary>Verify جداگانه ندارد؛ Confirm همان تسویه‌ی نهایی است (SettleAsync).</summary>
    public Task<GatewayOperationResult> VerifyAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayOperationResult(true, "0", null));

    public async Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewayRefId))
            return new GatewayOperationResult(false, "NO_TOKEN", "توکن تراکنش یافت نشد");

        var res = await _api.ConfirmAsync(transaction.GatewayRefId, cancellationToken);
        if (res is null)
            return new GatewayOperationResult(false, "NO_RESPONSE", "پاسخ نامعتبر از پرداخت نوین");

        return res.Status is Success or AlreadyConfirmed
            ? new GatewayOperationResult(true, res.Status.ToString(), null)
            : new GatewayOperationResult(false, res.Status.ToString(), MapError(res.Status, null));
    }

    public async Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        // تراکنشی که Confirm نشده طبق مستند بعد از ۱۵ دقیقه خودکار برگشت می‌خورد
        if (transaction.PaymentStatusId != EPaymentStatus.Settled)
            return new GatewayOperationResult(true, "0", null);

        if (string.IsNullOrWhiteSpace(transaction.GatewayRefId))
            return new GatewayOperationResult(false, "NO_TOKEN", "توکن تراکنش یافت نشد");

        var res = await _api.ReverseAsync(transaction.GatewayRefId, cancellationToken);
        if (res is null)
            return new GatewayOperationResult(false, "NO_RESPONSE", "پاسخ نامعتبر از پرداخت نوین");

        return res.Status == Success
            ? new GatewayOperationResult(true, "0", null)
            : new GatewayOperationResult(false, res.Status.ToString(),
                MapError(res.Status, res.Message) + " (برگشت وجه فقط تا ۱۵ دقیقه بعد از تأیید ممکن است)");
    }

    private static string? Get(IDictionary<string, string> p, string key) =>
        p.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string MapError(short code, string? bankMessage) => code switch
    {
        3 => "پذیرنده نامعتبر است",
        5 => "از انجام تراکنش صرف نظر شد",
        12 => "تراکنش نامعتبر است",
        13 => "مبلغ نامعتبر است",
        14 => "شماره کارت نامعتبر است",
        25 => "تراکنش اصلی یافت نشد",
        51 => "موجودی کافی نیست",
        54 => "تاریخ انقضای کارت گذشته است",
        55 => "رمز کارت نامعتبر است",
        61 => "مبلغ بیش از سقف مجاز کارت است",
        65 => "تعداد درخواست تراکنش بیش از حد مجاز است",
        91 => "بانک صادرکننده‌ی کارت پاسخگو نیست",
        -138 => "پرداخت توسط کاربر لغو شد",
        _ => $"خطای شماره {code} از سوی پرداخت نوین" +
             (string.IsNullOrWhiteSpace(bankMessage) ? "" : $" ({bankMessage})")
    };
}