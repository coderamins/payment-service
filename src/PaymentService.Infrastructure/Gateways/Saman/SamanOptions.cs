using System.Text.Json.Serialization;

namespace PaymentService.Infrastructure.Gateways.Saman;

public class SamanOptions
{
    public const string SectionName = "PaymentGateways:Saman";

    public int TerminalId { get; set; }

    public string ApiBaseUrl { get; set; } = default!;

    /// <summary>صفحه‌ای که فرم RefId با POST به آن ارسال می‌شود</summary>
    public string PaymentPageUrl { get; set; } = "https://sep.shaparak.ir";

    public int TimeoutSeconds { get; set; } = 30;
}
