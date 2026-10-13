using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Gateways.Helpers;
using PaymentService.Infrastructure.Gateways.Shaparak;

namespace PaymentService.Infrastructure.Gateways.AsanPardakht;

public class AsanPardakhtGateway : IPaymentGateway
{
    private readonly AsanPardakhtApiClient _apiClient;
    private readonly AsanPardakhtOptions _options;
    private readonly ShaparakOptions _shaparakOptions;
    private readonly ILogger<AsanPardakhtGateway> _logger;

    public GatewayProvider Provider => GatewayProvider.AsanPardakht;

    public AsanPardakhtGateway(
        AsanPardakhtApiClient apiClient,
        AsanPardakhtOptions options,
        ShaparakOptions shaparakOptions,
        ILogger<AsanPardakhtGateway> logger
        )
    {
        _apiClient = apiClient;
        _options = options;
        _logger = logger;
        _shaparakOptions = shaparakOptions;
    }

    public async Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction,
        string callbackUrl,
        string? payerNationalCode = null,
        CancellationToken cancellationToken = default)
    {
        var additionalData = BuildAdditionalData(payerNationalCode);

        var (isSuccess, token, statusCode) = await _apiClient.RequestTokenAsync(
            transaction.Id, transaction.AmountInRials,
            callbackUrl, additionalData, cancellationToken);

        if (!isSuccess || string.IsNullOrWhiteSpace(token))
            return new GatewayRequestResult(false, null, null, MapTokenError(statusCode));

        return new GatewayRequestResult(
            IsSuccess: true,
            RefId: token,
            RedirectUrl: _options.PaymentPageUrl,
            ErrorMessage: null,
            RedirectMethod: "POST",
            RedirectFormFields: new Dictionary<string, string> { ["RefId"] = token });
    }

    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> rawParameters)
    {
        var payGateTranId = GetParameter(rawParameters, "PayGateTranID");

        var isSuccess = long.TryParse(payGateTranId, out var tranId) && tranId > 0;

        return new GatewayCallbackParseResult(
            isSuccess,
            isSuccess ? "0" : "911",
            SaleReferenceId: payGateTranId,   // در مراحل Verify/Settle به همین نیاز داریم
            MaskedCardNumber: null,           // فقط با بازگشایی ReturningParams در دسترس است
            isSuccess ? null : "پرداخت توسط کاربر لغو شد یا ناموفق بود");
    }

    public async Task<GatewayOperationResult> VerifyAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        var payGateTranId = await ResolvePayGateTranIdAsync(transaction, cancellationToken);
        if (payGateTranId is null)
            return new GatewayOperationResult(false, null, "شناسه مرجع تراکنش در دسترس نیست");

        var response = await _apiClient.VerifyAsync(payGateTranId.Value, cancellationToken);
        var code = (int)response.StatusCode;

        if (code == 200 || code == 472)
            return new GatewayOperationResult(true, code.ToString(), null);

        return new GatewayOperationResult(false, code.ToString(), MapVerifyError(code));
    }

    public async Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        var payGateTranId = await ResolvePayGateTranIdAsync(transaction, cancellationToken);
        if (payGateTranId is null)
            return new GatewayOperationResult(false, null, "شناسه مرجع تراکنش در دسترس نیست");

        var response = await _apiClient.SettlementAsync(payGateTranId.Value, cancellationToken);
        var code = (int)response.StatusCode;

        // ۴۷۴ یعنی قبلاً تسویه شده 
        if (code == 200 || code == 474 || code == 476)
            return new GatewayOperationResult(true, code.ToString(), null);

        return new GatewayOperationResult(false, code.ToString(), MapSettlementError(code));
    }

    /// <summary>
    /// توجه: تراکنشی که Settlement شده باشد با هیچ‌کدام قابل برگشت نیست.
    /// </summary>
    public async Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        var payGateTranId = await ResolvePayGateTranIdAsync(transaction, cancellationToken);
        if (payGateTranId is null)
            return new GatewayOperationResult(false, null, "شناسه مرجع تراکنش در دسترس نیست");

        var useCancel = transaction.Status is PaymentStatus.Verified or PaymentStatus.Settled;

        var response = useCancel
            ? await _apiClient.CancelAsync(payGateTranId.Value, cancellationToken)
            : await _apiClient.ReverseAsync(payGateTranId.Value, cancellationToken);

        var code = (int)response.StatusCode;
        if (code == 200)
            return new GatewayOperationResult(true, "200", null);

        return new GatewayOperationResult(false, code.ToString(),
            $"برگشت وجه ناموفق بود (کد {code})");
    }


    private async Task<long?> ResolvePayGateTranIdAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        if (long.TryParse(transaction.GatewaySaleReferenceId, out var stored) && stored > 0)
            return stored;

        var tranResult = await _apiClient.GetTransactionResultAsync(transaction.Id, ct);
        return tranResult?.PayGateTranId > 0 ? tranResult.PayGateTranId : null;
    }

    private static string MapTokenError(int code) => code switch
    {
        469 => "شماره فاکتور ارسالی نامعتبر است",
        473 => "نام کاربری یا رمز عبور اشتباه است",
        474 => "آی‌پی سرور پذیرنده نزد آسان پرداخت مجاز نیست",
        485 => "تاریخ و زمان ارسالی نامعتبر است",
        486 => "مبلغ خارج از سقف مجاز پذیرنده است",
        488 => "آدرس بازگشت (CallbackUrl) نامعتبر است",
        489 => "شماره فاکتور تکراری است",
        490 => "پذیرنده غیرفعال است",
        _ => $"ایجاد توکن پرداخت ناموفق بود (کد {code})"
    };

    private static string MapVerifyError(int code) => code switch
    {
        471 => "تراکنش موفق نبوده است",
        473 => "تراکنش قبلاً تسویه شده است",
        474 => "تراکنش برگشت داده شده است",
        478 => "برای این تراکنش درخواست لغو ثبت شده و امکان تایید ندارد",
        572 => "تراکنش یافت نشد",
        _ => $"تایید تراکنش ناموفق بود (کد {code})"
    };

    private static string MapSettlementError(int code) => code switch
    {
        471 => "تراکنش موفق نبوده است",
        472 => "تراکنش هنوز تایید (Verify) نشده است",
        473 => "تراکنش برگشت داده شده است",
        478 => "برای این تراکنش درخواست لغو ثبت شده است",
        572 => "تراکنش یافت نشد",
        _ => $"تسویه تراکنش ناموفق بود (کد {code})"
    };

    private string? BuildAdditionalData(string? payerNationalCode)
    {
        if (string.IsNullOrWhiteSpace(payerNationalCode))
            return null;

        const int personalityTypeIndividualIranian = 210;

        // رشته‌ی گسترش تصادفی: عددی با ۱ تا 8 رقم، طبق N(1,7)
        var randomSalt = Random.Shared.Next(10_000_000, 100_000_000);

        var plainText = $"{personalityTypeIndividualIranian}|{payerNationalCode}|{randomSalt}";

        var key = Convert.FromBase64String(_shaparakOptions.EncryptionKey);
        var iv = Convert.FromBase64String(_shaparakOptions.EncryptionIV);

        var encryptedNationalId = Aes256CbcHelper.Encrypt(
            payerNationalCode, 
            randomSalt.ToString(),
            encryptionKeyBase64: _shaparakOptions.EncryptionKey, 
            encryptionIvBase64: _shaparakOptions.EncryptionIV);

        return $"{{'EncryptedNationalId':'{encryptedNationalId}'}}";
    }

    private static string? GetParameter(IDictionary<string, string> parameters, string name)
    {
        foreach (var (key, value) in parameters)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

}
