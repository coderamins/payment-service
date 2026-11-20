using Azure;
using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using Serilog;

namespace PaymentService.Infrastructure.Gateways.Saman;

public class SamanGateway : IPaymentGateway
{
    private readonly SamanApiClient _apiClient;
    private readonly SamanOptions _options;
    private readonly ILogger<SamanGateway> _logger;
    private readonly IPaymentTransactionRepository _repository;

    public GatewayProvider Provider => GatewayProvider.Saman;

    public SamanGateway(SamanApiClient apiClient, SamanOptions options, ILogger<SamanGateway> logger, IPaymentTransactionRepository repository)
    {
        _apiClient = apiClient;
        _options = options;
        _logger = logger;
        _repository = repository;
    }

    public async Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction,
        string callbackUrl,
        string? payerNationalCode = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.RequestTokenAsync(
            transaction.PaymentTransactionId, 
            transaction.AmountInRials, 
            callbackUrl, 
            null/*TODO: Mobile*/, 
            cancellationToken);

        if (response.Status < 0)
        {
            _logger.LogWarning("RequestTokenAsync سامان برای سفارش {OrderId} ناموفق بود. Status={Status}",
                transaction.OrderId, response.Status);
            return new GatewayRequestResult(false, null, null, $"{response.ErrorCode} - {response.ErrorDesc}");
        }

        // کاربر باید با GET به این آدرس هدایت شود (نه POST)
        var redirectUrl = $"{_options.PaymentPageUrl.TrimEnd('/')}/?token={response.Token}";

        return new GatewayRequestResult(true, response.Token.ToString(), redirectUrl, null);
    }

    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> rawParameters)
    {
        /*
            rawParameters:
            ▪ MID : شماره ترمینال
            ▪ State : وضعیت تراکنش - حروف انگلیسی
            ▪ Status : وضعیت تراکنش - مقدار عددی
            ▪ RRN : شماره مرجع
            ▪ RefNum : رسید دیجیتالی خرید
            ▪ ResNum : شماره خرید
            ▪ TerminalId : شماره ترمینال
            ▪ TraceNo : شماره رهگیری 
            ▪ Amount
            ▪ Wage
            ▪ SecurePan : شماره کارتی که تراکنش با آن انجام شده است
            ▪ HashedCardNumber : شماره کارت هش شده SHA256
        */
        var state = GetParameter(rawParameters, "State");
        var statusText = GetParameter(rawParameters, "Status");
        var rrnText = GetParameter(rawParameters, "RRN");
        var refNum = GetParameter(rawParameters, "RefNum");
        var securePan = GetParameter(rawParameters, "SecurePan");
        var amountRaw = GetParameter(rawParameters, "Amount");

        long? reportedAmount = long.TryParse(amountRaw, out var amount) ? amount : null;

        if (!int.TryParse(statusText, out var iStatusCode)) iStatusCode = -999;

        var isSuccess = (iStatusCode == 2);

        return new GatewayCallbackParseResult(
            isSuccess,
            statusText ?? "unknown",
            SaleReferenceId: isSuccess ? refNum : null,
            MaskedCardNumber: securePan,
            isSuccess ? null : MapCallbackError(iStatusCode),
            reportedAmount,
            null
            );
    }

    /// <summary>سامان Verify اولیه ندارد؛ Verify اصلی همان اقدام نهایی است و در SettleAsync انجام می‌شود.</summary>
    public Task<GatewayOperationResult> VerifyAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayOperationResult(true, "0", null));

    public async Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewaySaleReferenceId))
            return new GatewayOperationResult(false, "NO_SALE_REF", "شناسه مرجع سامان موجود نیست");

        var response = await _apiClient.ConfirmAsync(
            transaction.GatewaySaleReferenceId, _options.TerminalId, cancellationToken);

        if (response.ResultCode != 0)
            return new GatewayOperationResult(false, response.ResultCode.ToString(),
                MapResultCodeMessage(response.ResultCode));

        var detail = response.TransactionDetail;
        if (detail is null || detail.OrginalAmount != transaction.AmountInRials)
        {
            _logger.LogCritical("مغایرت مبلغ سامان. تراکنش {Id} ما {Ours} بانک {Theirs}",
                transaction.PaymentTransactionId, transaction.AmountInRials, detail?.OrginalAmount);
            try { await _apiClient.ReversalAsync(transaction.GatewaySaleReferenceId, _options.TerminalId, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Reverse بعد از مغایرت ناموفق بود"); }
            return new GatewayOperationResult(false, GatewayResultCodes.AmountMismatch, "مغایرت مبلغ با بانک");
        }

        return new GatewayOperationResult(true, "0", null, detail.MaskedPan);
    }

    public async Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(transaction.GatewayRefId, out var token))
            return new GatewayOperationResult(false, null, "توکن سامان برای این تراکنش در دسترس نیست");

        var response = await _apiClient.ReversalAsync(
            transaction.GatewaySaleReferenceId, _options.TerminalId, cancellationToken);

        var ok = response.ResultCode == 0;
        return new GatewayOperationResult(
            ok, response.ResultCode.ToString(),
            response.ResultDescription,
            response.TransactionDetail?.MaskedPan);
    }

    private static string? GetParameter(IDictionary<string, string> parameters, string name)
    {
        foreach (var (key, value) in parameters)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        return null;
    }

    private static string MapCallbackError(int statusCode) => statusCode switch
    {
        1 => "کاربر انصراف داده است",//CanceledByUser
        2 => "پرداخت با موفقیت انجام شد",//OK
        3 => "پرداخت انجام نشد",//Failed
        4 => "کاربر در بازه زمانی تعیین شده پاسخی ارسال نکرده است",//SessionIsNull
        5 => "پارامترهای ارسالی نامعتبر است",//InvalidParameters
        8 => "آدرس سرور پذیرنده نامعتبر است (در پرداخت های بر پایه توکن)",//MerchantIpAddressIsInvalid
        10 => "توکن ارسال شده یافت نشد",//TokenNotFound
        11 => "با این شماره ترمینال فقط تراکنش های توکنی قابل پرداخت هستند",//TokenRequired
        12 => "شماره ترمینال ارسال شده یافت نشد",//TerminalNotFound
        21 => "محدودیت های مدل چند حسابی رعایت نشده",//MultisettlePolicyErrors
        _ => $"وضعیت تراکنش نامشخص (کد {statusCode})"
    };

    private static string MapResultCodeMessage(int code) => code switch
    {
        -2 => "تراکنش یافت نشد",
        -6 => "بیش از نیم ساعت از زمان اجرای تراکنش گذشته است",
        0 => "عملیات با موفقیت انجام شد",
        2 => "درخواست تکراری می باشد",
        -104 => "ترمینال ارسالی غیرفعال می باشد",
        -105 => "ترمینال ارسالی در سیستم موجود نمی باشد",
        -106 => "آدرس آی پی درخواستی غیر مجاز می باشد",
        _ => $"تراکنش ناموفق بود (کد {code})"
    };
}
