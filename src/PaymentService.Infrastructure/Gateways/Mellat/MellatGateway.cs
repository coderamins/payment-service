using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Gateways.Mellat;

/// <summary>
/// درگاه پرداخت بانک ملت
/// </summary>
public class MellatGateway : IPaymentGateway
{
    // کد نتیجه صفر یعنی موفقیت‌آمیز بودن عملیات - طبق مستندات بانک ملت
    private const string SuccessResultCode = "0";
    /// <summary>
    /// تراکنش پیشتر وریفای شده است
    /// </summary>
    private const string AlreadyVerifiedResultCode = "43";
    /// <summary>
    /// تراکنش پیشتر تسویه شده است
    /// </summary>
    private const string AlreadySettledResultCode = "45";

    /// <summary>
    /// اين تراکنش پيشتر به درخواست پذيرنده و يا بصورت
    /// اتوماتيک به دليل اتمام زمان مجاز برای تائيدآنReverse
    /// شده و وجه آن به دارنده عودت داده شده است
    /// </summary>
    private const string AlreadyReverseStatusCode = "48";

    private readonly MellatSoapClient _soapClient;
    private readonly MellatOptions _options;
    private readonly ILogger<MellatGateway> _logger;

    public GatewayProvider Provider => GatewayProvider.Mellat;

    public MellatGateway(MellatSoapClient soapClient, MellatOptions options, ILogger<MellatGateway> logger)
    {
        _soapClient = soapClient;
        _options = options;
        _logger = logger;
    }

    public async Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction,
        string callbackUrl,
        string? payerNationalCode = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;

        var mellatOrderId = transaction.PaymentTransactionId.ToString();

        var rawResult = await _soapClient.BpPayRequestAsync(
            orderId: mellatOrderId,
            amount: transaction.AmountInRials,
            localDate: now.ToString("yyyyMMdd"),
            localTime: now.ToString("HHmmss"),
            callbackUrl: callbackUrl,
            additionalData: $"orderRef:{transaction.OrderId}",
            payerId: null,
            ct: cancellationToken);

        var parts = rawResult.Split(',');
        var resultCode = parts[0];

        if (resultCode != SuccessResultCode)
        {
            _logger.LogWarning("bpPayRequest برای سفارش {OrderId} با کد خطا {ResultCode} برگشت خورد", transaction.OrderId, resultCode);
            return new GatewayRequestResult(false, null, null, MapErrorMessage(resultCode));
        }

        var refId = parts.Length > 1 ? parts[1] : throw new InvalidOperationException("RefId در پاسخ موفق بانک ملت یافت نشد");
        var redirectUrl = $"{_options.PaymentPageBaseUrl}?RefId={refId}";

        return new GatewayRequestResult(true, refId, redirectUrl, null);
    }

    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> rawParameters)
    {
        // بانک ملت بعد از پرداخت این پارامترها را با POST به CallBackUrl ارسال می‌کند:
        // ResCode, SaleOrderId, SaleReferenceId, CardHolderPan, RefId
        rawParameters.TryGetValue("ResCode", out var resCode);
        rawParameters.TryGetValue("SaleReferenceId", out var saleReferenceId);
        rawParameters.TryGetValue("CardHolderPan", out var maskedCard);
        rawParameters.TryGetValue("RefId", out var refId);

        rawParameters.TryGetValue("Amount", out var amountRaw);

        long? reportedAmount = long.TryParse(amountRaw, out var amount) ? amount : null;

        var isSuccess = resCode == SuccessResultCode;

        return new GatewayCallbackParseResult(
            isSuccess,
            resCode ?? "unknown",
            (saleReferenceId is "0" or "-1" ? null: saleReferenceId),
            maskedCard,
            isSuccess ? null : MapErrorMessage(resCode ?? "unknown"),
            reportedAmount,
            refId,
            true
            );
    }

    public async Task<GatewayOperationResult> VerifyAsync(
       PaymentTransaction transaction,
       CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewaySaleReferenceId))
            return new GatewayOperationResult(false, "NO_SALE_REF", "شناسه مرجع فروش موجود نیست");

        var mellatOrderId = transaction.PaymentTransactionId.ToString();
        var resultCode = (await _soapClient.BpVerifyRequestAsync(
            mellatOrderId, mellatOrderId, transaction.GatewaySaleReferenceId, cancellationToken))?.Trim();

        //---اگه بار اول وریفای موفق شده باشه ولی به هردلیلی نتیجه برای ما برنگشته باشه دفه بعد که کانفیرم میکنیم کد 43 برگردانده می شود 
        var isSuccess = resultCode is SuccessResultCode or AlreadyVerifiedResultCode;   

        return new GatewayOperationResult(isSuccess, resultCode, isSuccess ? null : MapErrorMessage(resultCode));
    }

    public async Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var mellatOrderId = transaction.PaymentTransactionId.ToString();
        var resultCode = (await _soapClient.BpSettleRequestAsync(
            mellatOrderId, mellatOrderId, transaction.GatewaySaleReferenceId!, cancellationToken))?.Trim();

        //--- کد 45 وقتی برمیگرده که دفه اول تسویه شده ولی نتیجه برنگشته ولی دفه دوم با موفقیت برمیگرده
        var isSuccess = resultCode is SuccessResultCode or AlreadySettledResultCode;

        return new GatewayOperationResult(isSuccess, resultCode, isSuccess ? null : MapErrorMessage(resultCode));
    }

    public async Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var mellatOrderId = transaction.PaymentTransactionId.ToString();
        var resultCode = (await _soapClient.BpReversalRequestAsync(
            mellatOrderId, mellatOrderId, transaction.GatewaySaleReferenceId!, cancellationToken))?.Trim();

        //---اگه دفه اول در ریورس نتیجه دریافت نشده باشه ولی ریورس انجام شده باشه دفعات بعدی کد 48 بجای 0 برمیگرده 
        var isSuccess = resultCode is SuccessResultCode or AlreadyReverseStatusCode;

        return new GatewayOperationResult(isSuccess, resultCode, isSuccess ? null : MapErrorMessage(resultCode));
    }

    /// <summary>کد های خطای بانک ملت </summary>
    private static string MapErrorMessage(string resultCode) => resultCode switch
    {
        "11" => "شماره کارت نامعتبر است",
        "12" => "موجودی کافی نیست",
        "13" => "رمز نادرست است",
        "14" => "تعداد دفعات وارد کردن رمز بیش از حد مجاز است",
        "15" => "کارت نامعتبر است",
        "16" => "دفعات برداشت وجه بیش از حد مجاز است",
        "17" => "کاربر از انجام تراکنش منصرف شده است",
        "18" => "تاریخ انقضای کارت گذشته است",
        "19" => "مبلغ برداشت وجه بیش از حد مجاز است",
        "111" => "صادر کننده کارت نامعتبر است",
        "412" => "شناسه قبض/پرداخت اشتباه است",
        _ => $"خطای شماره {resultCode} از سوی بانک ملت برگشت داده شد"
    };
}
