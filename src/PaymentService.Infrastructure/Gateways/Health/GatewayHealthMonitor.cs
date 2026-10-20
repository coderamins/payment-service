using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Gateways.Health;

/// <summary>
/// وضعیت سلامت هر درگاه را در حافظه نگه می‌دارد (Singleton).
/// هر instance از سرویس وضعیت مستقل خودش را دارد.
/// </summary>
public sealed class GatewayHealthMonitor : IGatewayHealthMonitor
{
    private readonly IOptionsMonitor<GatewayHealthOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GatewayHealthMonitor> _logger;
    private readonly ConcurrentDictionary<GatewayProvider, Circuit> _circuits = new();

    public GatewayHealthMonitor(
        IOptionsMonitor<GatewayHealthOptions> options,
        TimeProvider time,
        ILogger<GatewayHealthMonitor> logger)
    {
        _options = options;
        _time = time;
        _logger = logger;
    }

    public GatewayAdmission TryAcquire(GatewayProvider provider)
    {
        var options = _options.CurrentValue;
        if (IsManuallyDisabled(provider, options))
            return new GatewayAdmission(false, $"درگاه {provider} به‌صورت دستی غیرفعال شده است");

        var circuit = GetCircuit(provider);
        var now = Now();

        lock (circuit)
        {
            switch (circuit.State)
            {
                case GatewayCircuitState.Closed:
                    return new GatewayAdmission(true);

                case GatewayCircuitState.Open when now >= circuit.NextProbeAtUtc:
                    // زمان آزمایش رسیده؛ همین درخواست تنها درخواست آزمایشی است
                    circuit.State = GatewayCircuitState.HalfOpen;
                    circuit.ProbeStartedAtUtc = now;
                    _logger.LogInformation("درگاه {Provider} در حالت آزمایشی؛ یک درخواست اجازه عبور دارد", provider);
                    return new GatewayAdmission(true);

                case GatewayCircuitState.Open:
                    return Rejected(provider, circuit.NextProbeAtUtc - now);

                default: // HalfOpen
                    var probeDeadline = circuit.ProbeStartedAtUtc.AddSeconds(options.ProbeTimeoutSeconds);
                    if (now >= probeDeadline)
                    {
                        // درخواست آزمایشی قبلی نتیجه‌ای اعلام نکرد؛ اجازه آزمایش دوباره
                        circuit.ProbeStartedAtUtc = now;
                        return new GatewayAdmission(true);
                    }
                    return Rejected(provider, probeDeadline - now);
            }
        }
    }

    public void RecordSuccess(GatewayProvider provider)
    {
        var circuit = GetCircuit(provider);
        var options = _options.CurrentValue;

        lock (circuit)
        {
            switch (circuit.State)
            {
                case GatewayCircuitState.HalfOpen:
                    circuit.Reset();
                    _logger.LogInformation("درگاه {Provider} دوباره سالم شد و به سرویس‌دهی برگشت", provider);
                    break;

                case GatewayCircuitState.Closed:
                    circuit.ConsecutiveFailures = 0;
                    circuit.AddSample(true, options.SamplingWindowSize);
                    break;

                    // Open: نتیجه درخواستی که قبل از قطع شدن شروع شده؛ نادیده گرفته می‌شود
            }
        }
    }

    public void RecordFailure(GatewayProvider provider, string reason)
    {
        var circuit = GetCircuit(provider);
        var options = _options.CurrentValue;
        var now = Now();

        lock (circuit)
        {
            circuit.LastError = reason;

            switch (circuit.State)
            {
                case GatewayCircuitState.HalfOpen:
                    Trip(provider, circuit, options, now, "درخواست آزمایشی هم ناموفق بود");
                    break;

                case GatewayCircuitState.Closed:
                    circuit.ConsecutiveFailures++;
                    circuit.AddSample(false, options.SamplingWindowSize);
                    if (ShouldTrip(circuit, options))
                        Trip(provider, circuit, options, now, reason);
                    break;
            }
        }
    }

    public IReadOnlyList<GatewayHealthSnapshot> GetSnapshot()
    {
        var options = _options.CurrentValue;
        var now = Now();
        var result = new List<GatewayHealthSnapshot>();

        foreach (var provider in Enum.GetValues<GatewayProvider>())
        {
            var manual = IsManuallyDisabled(provider, options);
            var circuit = GetCircuit(provider);

            lock (circuit)
            {
                // وقتی زمان آزمایش رسیده، درگاه «قابل آزمایش» است و باید در لیست فعال‌ها بیاید.
                // وگرنه فرانت آن را مخفی می‌کند، هیچ درخواستی نمی‌آید و درگاه هیچ‌وقت برنمی‌گردد.
                var probeDue =
                    (circuit.State == GatewayCircuitState.Open && now >= circuit.NextProbeAtUtc) ||
                    (circuit.State == GatewayCircuitState.HalfOpen &&
                     now >= circuit.ProbeStartedAtUtc.AddSeconds(options.ProbeTimeoutSeconds));

                var available = !manual && (circuit.State == GatewayCircuitState.Closed || probeDue);

                TimeSpan? retryAfter = null;
                if (!manual && !available)
                {
                    retryAfter = circuit.State == GatewayCircuitState.Open
                        ? circuit.NextProbeAtUtc - now
                        : circuit.ProbeStartedAtUtc.AddSeconds(options.ProbeTimeoutSeconds) - now;
                }

                result.Add(new GatewayHealthSnapshot(
                    provider,
                    circuit.State,
                    available,
                    manual,
                    circuit.State == GatewayCircuitState.Open ? circuit.NextProbeAtUtc : null,
                    retryAfter,
                    circuit.LastError));
            }
        }

        return result;
    }

    private void Trip(GatewayProvider provider, Circuit circuit, GatewayHealthOptions options, DateTime now, string reason)
    {
        circuit.TripCount++;
        var seconds = Math.Min(
            options.OpenDurationSeconds * Math.Pow(2, circuit.TripCount - 1),
            options.MaxOpenDurationSeconds);

        circuit.State = GatewayCircuitState.Open;
        circuit.OpenedAtUtc = now;
        circuit.NextProbeAtUtc = now.AddSeconds(seconds);
        circuit.ConsecutiveFailures = 0;
        circuit.Samples.Clear();

        _logger.LogWarning(
            "درگاه {Provider} از سرویس‌دهی خارج شد برای {Seconds} ثانیه. علت: {Reason}",
            provider, seconds, reason);
    }

    private static bool ShouldTrip(Circuit circuit, GatewayHealthOptions options)
    {
        if (circuit.ConsecutiveFailures >= options.ConsecutiveFailureThreshold)
            return true;

        if (circuit.Samples.Count < options.MinimumCalls)
            return false;

        var failures = circuit.Samples.Count(ok => !ok);
        return (double)failures / circuit.Samples.Count >= options.FailureRatioThreshold;
    }

    private static GatewayAdmission Rejected(GatewayProvider provider, TimeSpan retryAfter)
        => new(false, $"درگاه {provider} موقتاً در دسترس نیست",
            retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter);

    private static bool IsManuallyDisabled(GatewayProvider provider, GatewayHealthOptions options)
        => options.DisabledGateways.Any(n =>
            string.Equals(n, provider.ToString(), StringComparison.OrdinalIgnoreCase));

    private Circuit GetCircuit(GatewayProvider provider)
        => _circuits.GetOrAdd(provider, _ => new Circuit());

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private sealed class Circuit
    {
        public GatewayCircuitState State = GatewayCircuitState.Closed;
        public int ConsecutiveFailures;
        public int TripCount;
        public DateTime OpenedAtUtc;
        public DateTime NextProbeAtUtc;
        public DateTime ProbeStartedAtUtc;
        public string? LastError;
        public readonly Queue<bool> Samples = new();

        public void AddSample(bool ok, int max)
        {
            Samples.Enqueue(ok);
            while (Samples.Count > max) Samples.Dequeue();
        }

        public void Reset()
        {
            State = GatewayCircuitState.Closed;
            ConsecutiveFailures = 0;
            TripCount = 0;
            Samples.Clear();
        }
    }
}