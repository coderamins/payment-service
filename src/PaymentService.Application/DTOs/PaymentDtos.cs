using PaymentService.Domain.Enums;

namespace PaymentService.Application.DTOs;

/// <summary>
/// درخواست ایجاد یک پرداخت جدید.
/// آدرس Callback بانک دیگر از کلاینت گرفته نمی‌شود؛ خود سرویس بعد از ساخت تراکنش آن را می‌سازد.
/// </summary>
public record CreatePaymentRequest(
    long OrderId,
    long AmountInRials,
    string RequestingService,   // سیستم فروش اینترنتی یا میز خدمت یا ... بصورت لاتین
    GatewayProvider GatewayProvider,
    string ReturnUrl,
    string? PayerNationalCode = null,
    string? Description = null
);

public record CreatePaymentResult(
    bool IsSuccess,
    long TransactionId,
    string? RedirectUrl,
    string? ErrorMessage,
    string RedirectMethod = "GET",
    IReadOnlyDictionary<string, string>? RedirectFormFields = null);

/// <summary>داده‌ای که درگاه پس از بازگشت کاربر (Callback) برای ما ارسال می‌کند</summary>
public record GatewayCallbackData(
    long TransactionId,
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
    long AmountInRials,
    string Status,
    string GatewayProvider,
    string? MaskedCardNumber,
    string? ReferenceCode,
    string? FailureReason,
    DateTime CreatedAtUtc
);


/// <summary>نتیجه ثبت بازگشت از درگاه (هنوز Verify/Settle انجام نشده)</summary>
public record GatewayCallbackResult(
    bool IsPaid, 
    long TransactionId, 
    string? ErrorMessage);