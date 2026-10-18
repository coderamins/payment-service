using global::PaymentService.Application.Abstractions;
using global::PaymentService.Domain.Entities;
using global::PaymentService.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace PaymentService.Infrastructure.Gateways.Parsian;

public class ParsianGateway : IPaymentGateway
{
    private readonly ParsianApiClient _apiClient;
    private readonly ParsianOptions _options;
    private readonly ILogger<ParsianGateway> _logger;

    public ParsianGateway(ParsianApiClient apiClient, ParsianOptions options, ILogger<ParsianGateway> logger)
    {
        _apiClient = apiClient;
        _options = options;
        _logger = logger;
    }

    public GatewayProvider Provider => GatewayProvider.Parsian;

    public async Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction,
        string callbackUrl,
        string? payerNationalCode = null,
        CancellationToken cancellationToken = default)
    {
        // OrderId پارسیان باید long و یکتا باشد؛ همان شناسه تراکنش خودمان
        var response = await _apiClient.SalePaymentAsync(
            transaction.Id, transaction.AmountInRials, callbackUrl,
            $"orderRef:{transaction.OrderId}", cancellationToken);

        if (response.Status != 0 || response.Token <= 0)
        {
            _logger.LogWarning("SalePaymentRequest پارسیان برای سفارش {OrderId} ناموفق بود. Status={Status}",
                transaction.OrderId, response.Status);
            return new GatewayRequestResult(false, null, null, MapError(response.Status, response.Message));
        }

        // کاربر باید با GET به این آدرس هدایت شود (نه POST)
        var redirectUrl = string.IsNullOrWhiteSpace(response.RedirectUrl)
            ? $"{_options.PaymentPageUrl.TrimEnd('/')}/?token={response.Token}"
            : response.RedirectUrl;

        return new GatewayRequestResult(true, response.Token.ToString(), redirectUrl, null);
    }

    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> rawParameters)
    {
        var statusText = GetParameter(rawParameters, "status");
        var rrnText = GetParameter(rawParameters, "RRN");

        var hasStatus = int.TryParse(statusText, out var status);
        var isSuccess = hasStatus && status == 0
                        && long.TryParse(rrnText, out var rrn) && rrn > 0;

        // HashCardNumber هش است؛ شماره کارت ماسک‌شده بعد از Confirm می‌آید
        return new GatewayCallbackParseResult(
            isSuccess,
            statusText ?? "unknown",
            SaleReferenceId: isSuccess ? rrnText : null,
            MaskedCardNumber: null,
            isSuccess ? null : (hasStatus && status == -138
                ? "پرداخت توسط کاربر لغو شد"
                : MapError(hasStatus ? status : -1, null)));
    }

    /// <summary>پارسیان Verify جدا ندارد؛ Confirm همان اقدام نهایی است و در SettleAsync انجام می‌شود.</summary>
    public Task<GatewayOperationResult> VerifyAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayOperationResult(true, "0", null));

    public async Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(transaction.GatewayRefId, out var token))
            return new GatewayOperationResult(false, null, "توکن پارسیان برای این تراکنش در دسترس نیست");

        // مبلغ باید دقیقاً همان مبلغ زمان دریافت توکن باشد
        var response = await _apiClient.ConfirmAsync(
            token, transaction.Id, transaction.AmountInRials, cancellationToken);

        var ok = response.Status == 0;
        return new GatewayOperationResult(
            ok, response.Status.ToString(),
            ok ? null : MapError(response.Status, null),
            response.CardNumberMasked);
    }

    public async Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(transaction.GatewayRefId, out var token))
            return new GatewayOperationResult(false, null, "توکن پارسیان برای این تراکنش در دسترس نیست");

        var response = await _apiClient.ReversalAsync(token, cancellationToken);

        // -102 و -1551 یعنی قبلاً برگشت خورده؛ برای تکرار امن (idempotent) موفق حساب می‌شود
        var ok = response.Status is 0 or -102 or -1551;
        return new GatewayOperationResult(
            ok, response.Status.ToString(), ok ? null : MapError(response.Status, response.Message));
    }

    private static string? GetParameter(IDictionary<string, string> parameters, string name)
    {
        foreach (var (key, value) in parameters)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        return null;
    }

    private static string MapError(int code, string? bankMessage) => code switch
    {
        -138 => "پرداخت توسط کاربر لغو شد",
        -132 => "مبلغ کمتر از حداقل مجاز (۲۰,۰۰۰ ریال) است",
        -131 => "توکن نامعتبر است",
        -130 => "توکن منقضی شده است",
        -126 => "کد شناسایی پذیرنده نامعتبر است",
        -112 => "شناسه سفارش تکراری است",
        -101 => "پذیرنده احراز هویت نشد (بررسی IP سرور و LoginAccount)",
        -100 => "پذیرنده غیرفعال است",
        -1528 => "اطلاعات پرداخت یافت نشد",
        -1531 => "تأیید تراکنش ناموفق امکان‌پذیر نیست",
        -1549 => "زمان مجاز برای برگشت تراکنش به پایان رسیده است",
        -1550 => "برگشت تراکنش در وضعیت فعلی امکان‌پذیر نیست",
        -1552 => "برگشت تراکنش مجاز نیست",
        51 => "موجودی کافی نیست",
        _ => !string.IsNullOrWhiteSpace(bankMessage)
            ? $"{bankMessage} (کد {code})"
            : $"عملیات ناموفق بود (کد {code})"
    };
}
