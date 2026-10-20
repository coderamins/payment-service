using PaymentService.Domain.Enums;

namespace PaymentService.Application.Abstractions;

public enum GatewayCircuitState
{
    /// <summary>سالم؛ پرداخت جدید پذیرفته می‌شود</summary>
    Closed = 0,
    /// <summary>قطع؛ پرداخت جدید تا زمان آزمایش بعدی رد می‌شود</summary>
    Open = 1,
    /// <summary>آزمایشی؛ فقط یک درخواست برای سنجش سلامت اجازه عبور دارد</summary>
    HalfOpen = 2
}

public record GatewayAdmission(bool Allowed, string? Reason = null, TimeSpan? RetryAfter = null);

public record GatewayHealthSnapshot(
    GatewayProvider Provider,
    GatewayCircuitState State,
    bool IsAvailable,
    bool ManuallyDisabled,
    DateTime? NextProbeAtUtc,
    TimeSpan? RetryAfter,
    string? LastError);

public interface IGatewayHealthMonitor
{
    /// <summary>آیا اجازه شروع پرداخت جدید روی این درگاه هست؟ (فقط برای شروع پرداخت صدا زده شود)</summary>
    GatewayAdmission TryAcquire(GatewayProvider provider);

    /// <summary>درگاه پاسخ داد (حتی اگر پاسخ منطقی «ناموفق» بود)</summary>
    void RecordSuccess(GatewayProvider provider);

    /// <summary>خطای ارتباطی: تایم‌اوت، قطع شبکه، خطای سرور بانک</summary>
    void RecordFailure(GatewayProvider provider, string reason);

    IReadOnlyList<GatewayHealthSnapshot> GetSnapshot();
}