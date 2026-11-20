using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using System.Globalization;

namespace PaymentService.Infrastructure.Gateways.Pasargad;

/// Request → purchase | Callback → redirect با queryString | Verify → verify-payment |
/// Settle → confirm-transactions | Reverse → reverse-transactions
public class PasargadGateway : IPaymentGateway
{
    private const int AlreadyVerified = 13428;
    private const int AlreadyConfirmed = 13029;
    private const int AlreadyReversed = 13033;

    private readonly PasargadApiClient _apiClient;
    private readonly ILogger<PasargadGateway> _logger;

    public PasargadGateway(PasargadApiClient apiClient, ILogger<PasargadGateway> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public GatewayProvider Provider => GatewayProvider.Pasargad;

    public async Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction,
        string callbackUrl,
        string? payerNationalCode = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.PurchaseAsync(
            Invoice(transaction),
            transaction.CreatedAt,
            (long)transaction.AmountInRials,
            callbackUrl,
            $"order:{transaction.OrderId}",
            payerNationalCode,
            transaction.TerminalNumber,
            transaction.GetSplits(),
            cancellationToken);

        var data = response.ReadData<PasargadPurchaseData>();

        if (!response.IsSuccess
            || data is null
            || string.IsNullOrWhiteSpace(data.UrlId)
            || string.IsNullOrWhiteSpace(data.Url))
        {
            _logger.LogWarning("purchase پاسارگاد برای سفارش {OrderId} ناموفق بود. Code={Code}",
                transaction.OrderId, response.Code);
            return new GatewayRequestResult(false, null, null, MapError(response.Code, response.ResultMsg));
        }

        // urlId در GatewayRefId ذخیره می‌شود و در verify/confirm/reverse لازم است
        return new GatewayRequestResult(true, data.UrlId, data.Url, null);
    }

    /// callback با GET: invoiceId، status (success/failed/unknown/cancel/expire)، referenceNumber، trackId
    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> rawParameters)
    {
        var status = GetParameter(rawParameters, "status")?.Trim().ToLowerInvariant();
        var referenceNumber = GetParameter(rawParameters, "referenceNumber");
        var trackId = GetParameter(rawParameters, "trackId");

        // فقط status ملاک است؛ صحت نهایی را Verify نزد بانک تعیین می‌کند
        var isSuccess = status == "success";

        return new GatewayCallbackParseResult(
            isSuccess,
            status ?? "unknown",
            SaleReferenceId: isSuccess ? (NullIfEmpty(referenceNumber) ?? NullIfEmpty(trackId)) : null,
            MaskedCardNumber: null,
            ErrorMessage: isSuccess ? null : MapCallbackStatus(status),
            ReportedAmountInRials: null,
            CallbackToken: null);
    }

    public async Task<GatewayOperationResult> VerifyAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewayRefId))
            return new GatewayOperationResult(false, null, "urlId پاسارگاد برای این تراکنش در دسترس نیست");

        var response = await _apiClient.VerifyAsync(
            Invoice(transaction), transaction.GatewayRefId, cancellationToken);

        var ok = response.Code is 0 or AlreadyVerified or AlreadyConfirmed;
        if (!ok)
            return new GatewayOperationResult(
                false, response.Code.ToString(CultureInfo.InvariantCulture), MapError(response.Code, response.ResultMsg));

        var data = response.ReadData<PasargadTransactionData>();

        // مبلغ در callback نیست؛ اینجا با مبلغ سفارش مقایسه می‌شود
        if (data?.Amount is long bankAmount && bankAmount != (long)transaction.AmountInRials)
        {
            _logger.LogError("مغایرت مبلغ پاسارگاد. تراکنش {Id} سفارش={Expected} بانک={Actual}",
                transaction.PaymentTransactionId, (long)transaction.AmountInRials, bankAmount);

            return new GatewayOperationResult(
                false, GatewayResultCodes.AmountMismatch, "مغایرت مبلغ سفارش با مبلغ تایید‌شده توسط بانک",
                data.MaskedCardNumber)
            { ReportedAmountInRials = bankAmount };
        }

        return new GatewayOperationResult(true, "0", null, data?.MaskedCardNumber);
    }

    public async Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewayRefId))
            return new GatewayOperationResult(false, null, "urlId پاسارگاد برای این تراکنش در دسترس نیست");

        var response = await _apiClient.ConfirmAsync(
            Invoice(transaction), transaction.GatewayRefId, cancellationToken);

        var ok = response.Code is 0 or AlreadyConfirmed;
        var data = ok ? response.ReadData<PasargadTransactionData>() : null;

        return new GatewayOperationResult(
            ok,
            response.Code.ToString(CultureInfo.InvariantCulture),
            ok ? null : MapError(response.Code, response.ResultMsg),
            data?.MaskedCardNumber);
    }

    public async Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewayRefId))
            return new GatewayOperationResult(false, null, "urlId پاسارگاد برای این تراکنش در دسترس نیست");

        var response = await _apiClient.ReverseAsync(
            Invoice(transaction), transaction.GatewayRefId, cancellationToken);

        var ok = response.Code is 0 or AlreadyReversed;
        return new GatewayOperationResult(
            ok,
            response.Code.ToString(CultureInfo.InvariantCulture),
            ok ? null : MapError(response.Code, response.ResultMsg));
    }

    // ---------------------------------------------------------------- ابزارها

    private static string Invoice(PaymentTransaction t)
        => t.PaymentTransactionId.ToString(CultureInfo.InvariantCulture);

    private static string? GetParameter(IDictionary<string, string> parameters, string name)
    {
        foreach (var (key, value) in parameters)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        return null;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string MapCallbackStatus(string? status) => status switch
    {
        "cancel" => "پرداخت توسط کاربر لغو شد",
        "expire" => "زمان پرداخت منقضی شد",
        "failed" => "پرداخت ناموفق بود",
        "unknown" => "وضعیت پرداخت نامشخص است",
        _ => "نتیجه‌ی پرداخت نامعتبر است"
    };

    private static string MapError(int code, string? bankMessage) => code switch
    {
        1 => "پرداخت ناموفق بود",
        2 => "وضعیت تراکنش نامشخص است",
        401 => "احراز هویت نزد پاسارگاد ناموفق بود",
        500 => "خطای داخلی سرور پاسارگاد",
        13000 => "ورودی نامعتبر است",
        13001 or 13002 or 13009 => "توکن نامعتبر است",
        13003 => "شماره فاکتور تکراری است",
        13005 => "عدم امکان دسترسی موقت به سرور پاسارگاد",
        13008 => "توکن منقضی شده است",
        13010 => "شماره موبایل نامعتبر است",
        13016 => "تراکنش یافت نشد",
        13025 => "تراکنش ناموفق است",
        13026 => "شماره کارت نامعتبر است",
        13027 => "شماره موبایل برای شماره کارت وارد شده نیست",
        13028 => "آدرس کالبک نامعتبر است (دامنه باید نزد پاسارگاد ثبت شده باشد)",
        13029 => "تراکنش قبلاً تایید شده است",
        13030 => "تراکنش تایید شده و ناموفق است",
        13031 => "تراکنش منتظر تایید است",
        13033 => "تراکنش قبلاً برگشت خورده است",
        13034 => "وضعیت تراکنش نامشخص است؛ verify موفق دریافت شده و تاییدیه ایجاد خواهد شد",
        13045 => "دارنده کارت تراکنش را لغو کرد",
        13046 => "تراکنش تسویه شده است",
        13054 => "تراکنش نامعتبر است",
        13055 => "مبلغ تراکنش نامعتبر است",
        13063 => "موجودی کافی نیست",
        13075 => "زمان تاییدیه منقضی شده است",
        13076 => "زمان برگشت تراکنش منقضی شده است",
        13077 => "تراکنش اصلی نامعتبر است",
        13099 => "پایانه فروش نامعتبر است",
        13300 => "پایانه در سیستم غیرفعال است",
        13301 => "فروشگاه در سیستم تعریف نشده است",
        13402 => "ارجاع‌دهنده (Referer) با اطلاعات ثبت‌شده تطابق ندارد",
        13422 => "آی‌پی سرور پذیرنده نزد پاسارگاد معتبر نیست",
        13426 => "وضعیت تراکنش تایید شده نیست",
        13428 => "درخواست تاییدیه قبلاً ارسال شده است",
        13429 => "احراز هویت موفق نبود",
        13430 => "احراز هویت موفق نبود؛ بعداً دوباره تلاش کنید",
        _ => !string.IsNullOrWhiteSpace(bankMessage)
            ? $"{bankMessage} (کد {code})"
            : $"عملیات ناموفق بود (کد {code})"
    };
}