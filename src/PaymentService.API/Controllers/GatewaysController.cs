using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Abstractions;

namespace PaymentService.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/gateways")]
public class GatewaysController : ControllerBase
{
    private readonly IGatewayHealthMonitor _healthMonitor;

    public GatewaysController(IGatewayHealthMonitor healthMonitor)
        => _healthMonitor = healthMonitor;

    /// <summary>وضعیت درگاه‌ها؛ سیستم فروش فقط درگاه‌های IsAvailable را به کاربر نشان بدهد</summary>
    [HttpGet("availability")]
    public ActionResult<IEnumerable<GatewayAvailabilityDto>> GetAvailability()
        => Ok(_healthMonitor.GetSnapshot().Select(s => new GatewayAvailabilityDto(
            s.Provider.ToString(),
            (int)s.Provider,
            s.IsAvailable,
            s.State.ToString(),
            s.RetryAfter is { } r ? (int)Math.Ceiling(r.TotalSeconds) : (int?)null)));
}

public record GatewayAvailabilityDto(
    string Gateway, int GatewayProvider, bool IsAvailable, string State, int? RetryAfterSeconds);