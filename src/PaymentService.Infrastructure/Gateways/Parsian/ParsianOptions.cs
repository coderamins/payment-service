namespace PaymentService.Infrastructure.Gateways.Parsian;

public class ParsianOptions
{
    public const string SectionName = "PaymentGateways:Parsian";

    /// <summary>شناسه پذیرنده (LoginAccount) که پارسیان می‌دهد</summary>
    public string LoginAccount { get; set; } = default!;

    public string ApiBaseUrl { get; set; } = "https://pec.shaparak.ir/NewIPGRestServices/";

    /// <summary>فقط اگر RedirectUrl در پاسخ بانک نبود استفاده می‌شود</summary>
    public string PaymentPageUrl { get; set; } = "https://pec.shaparak.ir/NewIPG/";

    public int TimeoutSeconds { get; set; } = 30;
}