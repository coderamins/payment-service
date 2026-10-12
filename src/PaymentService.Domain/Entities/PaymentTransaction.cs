using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Entities;

public class PaymentTransaction
{
    public Guid Id { get; private set; }

    /// <summary>
    /// شماره سفارش/فاکتور در سیستم مبدا (مثلاً شماره سفارش خرید خودرو).
    /// </summary>
    public string OrderId { get; private set; } = default!;

    /// <summary>
    /// سرویس استفاده کننده
    /// </summary>
    public string RequestingService { get; private set; } = default!;

    /// <summary>مبلغ به ریال</summary>
    public long AmountInRials { get; private set; }

    /// <summary>درگاهی که این تراکنش از طریق آن پردازش شده/می‌شود</summary>
    public GatewayProvider GatewayProvider { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>شماره مرجع/RefId که درگاه هنگام bpPayRequest برمی‌گرداند</summary>
    public string? GatewayRefId { get; private set; }

    /// <summary>SaleReferenceId که بانک بعد از تراکنش موفق برمی‌گرداند (برای پیگیری‌های بعدی)</summary>
    public string? GatewaySaleReferenceId { get; private set; }

    /// <summary>
    /// شناسه فاکتور عددی یکتایی که به درگاه اعلام می‌شود.
    /// برخی درگاه‌ها (مانند آسان پرداخت) الزام می‌کنند این مقدار عددی و هرگز تکراری نباشد،
    /// و در مراحل بعدی (استعلام، تایید، تسویه) دوباره به همین مقدار نیاز است.
    /// </summary>
    public long GatewayInvoiceId { get; private set; }

    /// <summary>شماره کارت با فرمت ماسک‌شده (مثلاً 603799******1234) که بانک برمی‌گرداند</summary>
    public string? MaskedCardNumber { get; private set; }

    /// <summary>کد نتیجه خام برگشتی از درگاه (برای دیباگ و لاگ)</summary>
    public string? GatewayResultCode { get; private set; }

    /// <summary>پیام خطا در صورت ناموفق بودن </summary>
    public string? FailureReason { get; private set; }

    public string ReturnUrl { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private PaymentTransaction() { } 

    public static PaymentTransaction Create(
        string orderId,
        string requestingService,
        long amountInRials,
        string returnUrl,
        GatewayProvider gatewayProvider)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("شماره سفارش نمی‌تواند خالی باشد", nameof(orderId));

        if (amountInRials <= 0)
            throw new ArgumentException("مبلغ پرداخت باید بزرگتر از صفر باشد", nameof(amountInRials));

        if (string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Absolute))
            throw new ArgumentException("آدرس بازگشت (ReturnUrl) نامعتبر است", nameof(returnUrl));


        var invoiceId = long.Parse(
            DateTime.UtcNow.ToString("yyMMddHHmmssfff") + Random.Shared.Next(100, 999));

        return new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            GatewayInvoiceId = invoiceId,
            RequestingService = requestingService,
            AmountInRials = amountInRials,
            ReturnUrl = returnUrl,
            GatewayProvider = gatewayProvider,
            Status = PaymentStatus.Created,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public void MarkRedirectedToGateway(string gatewayRefId)
    {
        GatewayRefId = gatewayRefId;
        Status = PaymentStatus.RedirectedToGateway;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkCallbackReceived(string resultCode, string? saleReferenceId, string? maskedCardNumber)
    {
        GatewayResultCode = resultCode;
        GatewaySaleReferenceId = saleReferenceId;
        MaskedCardNumber = maskedCardNumber;
        Status = PaymentStatus.CallbackReceived;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkVerified()
    {
        Status = PaymentStatus.Verified;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkSettled()
    {
        Status = PaymentStatus.Settled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string reason, string? gatewayResultCode = null)
    {
        FailureReason = reason;
        GatewayResultCode = gatewayResultCode ?? GatewayResultCode;
        Status = PaymentStatus.Failed;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkCanceledByUser()
    {
        Status = PaymentStatus.CanceledByUser;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkReversed()
    {
        Status = PaymentStatus.Reversed;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
