using PaymentService.Domain.Enums;

namespace PaymentService.Application.DTOs;

/// <summary>
/// درخواست ایجاد یک پرداخت جدید.
/// آدرس Callback بانک دیگر از کلاینت گرفته نمی‌شود؛ خود سرویس بعد از ساخت تراکنش آن را می‌سازد.
/// </summary>
public record CreatePaymentRequest(
    long OrderId,
    long AmountInRials,
    EPaymentSubscriber PaymentSubscriber,  
    GatewayProvider GatewayProvider,
    string ReturnUrl,
    string? PayerNationalCode = null,
    string? Description = null,
    int? TerminalNumber = null,          // خالی = ترمینال پیش‌فرض (فعلاً فقط پاسارگاد)
    SplitValueType? SplitType = null,    // 1 = درصد، 2 = مبلغ (ریال)
    IReadOnlyList<PaymentSplit>? Splits = null   // خالی = پرداخت عادی یا تسهیم ثابت سمت بانک
);
public enum SplitValueType { Percent = 1, Amount = 2 }
public record PaymentSplit(string Sheba, decimal Value);

public record CreatePaymentResult(
    bool IsSuccess,
    long TransactionId,
    string? RedirectUrl,
    string? ErrorMessage,
    string RedirectMethod = "GET",
    IReadOnlyDictionary<string, string>? RedirectFormFields = null);

/// <summary>داده‌ای که درگاه پس از بازگشت کاربر (Callback) برای ما ارسال می‌کند</summary>
public record GatewayCallbackData(
    Guid CallbackKey,
    IDictionary<string, string> RawParameters
);

/// <summary>نتیجه نهایی پس از Verify + Settle</summary>
public record ConfirmPaymentResult(
    bool IsSuccess,
    long TransactionId,
    string? ReferenceCode,
    string? MaskedCardNumber,
    string? ErrorMessage
);

/// <summary>نتیجه استعلام وضعیت یک تراکنش</summary>
public record PaymentStatusResult(
    long TransactionId,
    long OrderId,
    decimal AmountInRials,
    PaymentOutcome Status,
    GatewayProvider GatewayProvider,
    string? MaskedCardNumber,
    string? ReferenceCode,
    string? GatewayRefId,
    string? FailureReason,
    string? StatusName,
    string? PaymentProviderName,
    DateTime CreatedAtUtc,
    DateTime? UpdateDateUtc
);


    