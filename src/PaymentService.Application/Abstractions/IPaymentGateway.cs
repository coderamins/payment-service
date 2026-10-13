using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Application.Abstractions;

public interface IPaymentGateway
{
    GatewayProvider Provider { get; }

    /// <summary>
    /// مرحله اول: درخواست پرداخت به بانک ارسال می‌شود و در صورت موفقیت
    /// آدرسی که کاربر باید برای وارد کردن اطلاعات کارت به آن هدایت شود برگردانده می‌شود.
    /// </summary>
    Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction,
        string callbackUrl,
        string? payerNationalCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// مرحله دوم: بعد از بازگشت کاربر از صفحه بانک، پارامترهای خام برگشتی
    /// (که ساختارشان بین بانک‌ها متفاوت است) پردازش و نتیجه استاندارد برگردانده می‌شود.
    /// </summary>
    GatewayCallbackParseResult ParseCallback(IDictionary<string, string> rawParameters);

    /// <summary>مرحله سوم: صحت‌سنجی تراکنش نزد بانک (bpVerifyRequest در ملت)</summary>
    Task<GatewayOperationResult> VerifyAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>مرحله چهارم: تسویه نهایی تراکنش نزد بانک (bpSettleRequest در ملت)</summary>
    Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>برگشت وجه/ابطال تراکنش در صورت نیاز (bpReversalRequest در ملت)</summary>
    Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken = default);
}

public record GatewayRequestResult(
    bool IsSuccess,
    string? RefId,
    string? RedirectUrl,
    string? ErrorMessage,
    string RedirectMethod = "GET",
    IReadOnlyDictionary<string, string>? RedirectFormFields = null);

public record GatewayCallbackParseResult(
    bool IsSuccessfulPayment,
    string ResultCode,
    string? SaleReferenceId,
    string? MaskedCardNumber,
    string? ErrorMessage);

public record GatewayOperationResult(
    bool IsSuccess, 
    string? ResultCode,
    string? ErrorMessage,
    string? MaskedCardNumber = null);

//string? MaskedCardNumber = null
//اینجا این اضافه شد چون پارسیان موقع 
//callback
//کارت ماسک شده را نمیدهد و اینکار رو موقع Confirm میده
