namespace PaymentService.Application.Common.Options;

/// <summary>
/// تنظیمات لازم برای ساخت آدرس Callback که بانک بعد از پرداخت کاربر را به آن هدایت می‌کند.
/// </summary>
public class PaymentCallbackOptions
{
    public const string SectionName = "Payment";
    public string PublicBaseUrl { get; set; } = default!;
}
