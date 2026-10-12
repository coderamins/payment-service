namespace PaymentService.Infrastructure.Gateways.Mellat;

public class MellatOptions
{
    public const string SectionName = "PaymentGateways:Mellat";

    /// <summary>شماره ترمینال دریافتی از بانک ملت</summary>
    public string TerminalId { get; set; } = default!;

    public string UserName { get; set; } = default!;

    public string Password { get; set; } = default!;

    /// <summary>آدرس وب‌سرویس SOAP بانک - پیش‌فرض آدرس رسمی شاپرک</summary>
    public string WebServiceUrl { get; set; } = "https://bpm.shaparak.ir/pgwchannel/services/pgw";

    /// <summary>آدرس صفحه‌ای که کاربر برای وارد کردن اطلاعات کارت به آن هدایت می‌شود</summary>
    public string PaymentPageBaseUrl { get; set; } = "https://bpm.shaparak.ir/pgwchannel/startpay.mellat";

    /// <summary>تایم‌اوت درخواست‌های SOAP به ثانیه</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
