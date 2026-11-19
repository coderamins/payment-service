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
            transaction.PaymentTransactionId, 
            transaction.AmountInRials,
            callbackUrl, 
            additionalData, 
            cancellationToken);

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
        var hint = GetParameter(rawParameters, "PayGateTranID");
        var looksPaid = hint is { Length: > 0 and <= 18 } && long.TryParse(hint, out var id) && id > 0;

        return new GatewayCallbackParseResult(
            looksPaid,
            looksPaid ? "0" : "911",
            SaleReferenceId: null,   
            MaskedCardNumber: null,           
            looksPaid ? null : "پرداخت توسط کاربر لغو شد یا ناموفق بود",
            null);
    }

    public async Task<GatewayOperationResult> VerifyAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        var tran=await _apiClient.GetTransactionResultAsync(transaction.PaymentTransactionId, cancellationToken);
        if (tran is null || tran.PayGateTranId <= 0)
            return new GatewayOperationResult(false, "NOT_FOUND", "تراکنش موفقی نزد بانک برای این تراکنش یافت نشد .");

        if(tran.Amount!=transaction.AmountInRials)
        {
            _logger.LogCritical(
                "مغایرت مبلغ در آسان‌پرداخت. تراکنش {Id} مبلغ ما {Ours} مبلغ بانک {Theirs}",
                transaction.PaymentTransactionId,
                transaction.AmountInRials,
                tran.Amount);

            try
            {
                await _apiClient.ReverseAsync(tran.PayGateTranId, cancellationToken);
            }
            catch (Exception ex) { _logger.LogError(ex, "Reverse بعد از مغایرت مبلغ ناموفق بود ."); }

            return new GatewayOperationResult(false, "AMOUNT_MISMATCH", "مغایرت مبلغ با بانک");
        }

        var response = await _apiClient.VerifyAsync(tran.PayGateTranId, cancellationToken);
        var code = (int)response.StatusCode;

        if (code == 200 || code == 472)
            return new GatewayOperationResult(true, code.ToString(), null);

        return new GatewayOperationResult(false, GatewayResultCodes.AmountMismatch, "مغایرت مبلغ با بانک")
        {
            ReportedAmountInRials = tran.Amount
        };
    }

    public async Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        var payGateTranId = await ResolvePayGateTranIdAsync(transaction, cancellationToken);
        if (payGateTranId is null)
            return new GatewayOperationResult(false, null, "شناسه مرجع تراکنش در دسترس نیست");

        var response = await _apiClient.SettlementAsync(payGateTranId.Value, cancellationToken);
        var code = (int)response.StatusCode;

        // 473 یعنی قبلاً تسویه شده 
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

        var useCancel = transaction.PaymentStatusId is EPaymentStatus.Verified or EPaymentStatus.Settled;

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
        var tran = await _apiClient.GetTransactionResultAsync(transaction.PaymentTransactionId, ct);
        return tran is { PayGateTranId: > 0 } ? tran.PayGateTranId : null;
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
