namespace PaymentService.Infrastructure.Gateways.Health;

public class GatewayHealthOptions
{
    public const string SectionName = "PaymentGateways:Health";

    /// <summary>تعداد شکست پشت‌سرهم برای قطع درگاه</summary>
    public int ConsecutiveFailureThreshold { get; set; } = 5;

    /// <summary>تعداد آخرین تماس‌هایی که برای محاسبه نرخ شکست نگه داشته می‌شود</summary>
    public int SamplingWindowSize { get; set; } = 20;

    /// <summary>حداقل تعداد تماس در پنجره تا نرخ شکست معتبر حساب شود</summary>
    public int MinimumCalls { get; set; } = 10;

    /// <summary>نرخ شکست (۰ تا ۱) که از آن به بعد درگاه قطع می‌شود</summary>
    public double FailureRatioThreshold { get; set; } = 0.5;

    public int OpenDurationSeconds { get; set; } = 30;
    public int MaxOpenDurationSeconds { get; set; } = 300;

    /// <summary>اگر درخواست آزمایشی نتیجه‌اش را اعلام نکرد، بعد از این مدت آزمایش دوباره مجاز است</summary>
    public int ProbeTimeoutSeconds { get; set; } = 30;

    /// <summary>درگاه‌هایی که دستی خاموش‌اند؛ مثلاً ["Mellat"]</summary>
    public List<string> DisabledGateways { get; set; } = new();
}