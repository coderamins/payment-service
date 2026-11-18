using PaymentService.Domain.Enums;
using System.Text.Json;

namespace PaymentService.Domain.Entities;

public class PaymentTransaction
{
    public long PaymentTransactionId { get; private set; }

    public Guid CallbackKey { get; private set; } = Guid.NewGuid();
    /// <summary>
    /// شماره سفارش/فاکتور در سیستم مبدا (مثلاً شماره سفارش خرید خودرو).
    /// </summary>
    public long OrderId { get; private set; } 

    /// <summary>
    /// سرویس استفاده کننده
    /// </summary>
    public EPaymentSubscriber PaymentSubscriberId { get; private set; } 

    /// <summary>مبلغ به ریال</summary>
    public decimal AmountInRials { get; private set; }

    /// <summary>درگاهی که این تراکنش از طریق آن پردازش شده/می‌شود</summary>
    public GatewayProvider GatewayProviderId { get; private set; }

    public EPaymentStatus PaymentStatusId { get; private set; }

    /// <summary>شماره مرجع/RefId که درگاه هنگام bpPayRequest برمی‌گرداند</summary>
    public string? GatewayRefId { get; private set; }

    /// <summary>SaleReferenceId که بانک بعد از تراکنش موفق برمی‌گرداند (برای پیگیری‌های بعدی)</summary>
    public string? GatewaySaleReferenceId { get; private set; }

    /// <summary>شماره کارت با فرمت ماسک‌شده (مثلاً 603799******1234) که بانک برمی‌گرداند</summary>
    public string? MaskedCardNumber { get; private set; }

    /// <summary>کد نتیجه خام برگشتی از درگاه (برای دیباگ و لاگ)</summary>
    public string? GatewayResultCode { get; private set; }

    /// <summary>پیام خطا در صورت ناموفق بودن </summary>
    public string? FailureReason { get; private set; }

    public string ReturnUrl { get; private set; } = default!;

    /// <summary>ترمینال تعیین‌شده توسط سیستم مبدا؛ null = ترمینال پیش‌فرض تنظیمات درگاه</summary>
    public int? TerminalNumber { get; private set; }

    /// <summary>تسهیم داینامیک (snapshot سهم‌ها به ریال)؛ null = بدون تسهیم یا تسهیم ثابت سمت بانک</summary>
    public string? SplitsJson { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? ModifiedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = default!;

    public void Touch() => ModifiedAt = DateTime.UtcNow;

    private PaymentTransaction() { } 

    public static PaymentTransaction Create(
        long orderId,
        EPaymentSubscriber paymentSubscriber,
        decimal amountInRials,
        string returnUrl,
        GatewayProvider gatewayProvider,
        int? terminalNumber = null,
        IReadOnlyList<PaymentSplitShare>? splits = null)
    {
        //if (orderId==null)
        //    throw new ArgumentException("شماره سفارش نمی‌تواند خالی باشد", nameof(orderId));

        if (amountInRials <= 0)
            throw new ArgumentException("مبلغ پرداخت باید بزرگتر از صفر باشد", nameof(amountInRials));

        if (string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Absolute))
            throw new ArgumentException("آدرس بازگشت (ReturnUrl) نامعتبر است", nameof(returnUrl));

        if (splits is { Count: > 0 } && splits.Sum(s => s.AmountInRials) != amountInRials)
            throw new ArgumentException("مجموع سهم‌های تسهیم باید برابر مبلغ تراکنش باشد", nameof(splits));

        return new PaymentTransaction
        {
            OrderId = orderId,
            PaymentSubscriberId = paymentSubscriber,
            AmountInRials = amountInRials,
            ReturnUrl = returnUrl,
            GatewayProviderId = gatewayProvider,
            TerminalNumber = terminalNumber,
            SplitsJson = splits is { Count: > 0 } ? JsonSerializer.Serialize(splits) : null,
            PaymentStatusId = EPaymentStatus.Created,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public IReadOnlyList<PaymentSplitShare> GetSplits()
    => string.IsNullOrEmpty(SplitsJson)
        ? Array.Empty<PaymentSplitShare>()
        : JsonSerializer.Deserialize<List<PaymentSplitShare>>(SplitsJson) ?? new List<PaymentSplitShare>();

    public void SetMaskedCardNumber(string maskedCardNumber)
    {
        MaskedCardNumber = maskedCardNumber;
        ModifiedAt = DateTime.UtcNow;
    }

    public void MarkRedirectedToGateway(string gatewayRefId)
    {
        GatewayRefId = gatewayRefId;
        PaymentStatusId = EPaymentStatus.RedirectedToGateway;
        ModifiedAt = DateTime.UtcNow;
    }

    public void MarkCallbackReceived(string resultCode, string? saleReferenceId, string? maskedCardNumber)
    {
        GatewayResultCode = resultCode;
        GatewaySaleReferenceId = saleReferenceId;
        MaskedCardNumber = maskedCardNumber;
        PaymentStatusId = EPaymentStatus.CallbackReceived;
        ModifiedAt = DateTime.UtcNow;
    }

    public void MarkVerified()
    {
        PaymentStatusId = EPaymentStatus.Verified;
        ModifiedAt = DateTime.UtcNow;
    }

    public void MarkSettled()
    {
        PaymentStatusId = EPaymentStatus.Settled;
        ModifiedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string reason, string? gatewayResultCode = null)
    {
        FailureReason = reason;
        GatewayResultCode = gatewayResultCode ?? GatewayResultCode;
        PaymentStatusId = EPaymentStatus.Failed;
        ModifiedAt = DateTime.UtcNow;
    }

    public void MarkCanceledByUser()
    {
        PaymentStatusId = EPaymentStatus.CanceledByUser;
        ModifiedAt = DateTime.UtcNow;
    }

    public void MarkReversed()
    {
        PaymentStatusId = EPaymentStatus.Reversed;
        ModifiedAt = DateTime.UtcNow;
    }

    public void MarkAmountMismatch()
    {
        PaymentStatusId = EPaymentStatus.AmountMismatch;
        ModifiedAt = DateTime.UtcNow;
    }
}
