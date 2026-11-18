using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;

namespace PaymentService.API.Extensions;

public static class RateLimitServiceExtension
{
    public static void AddRateLimitConfigs(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 429,
                    Title = "Too Many Requests",
                    Instance = ctx.HttpContext.Request.Path,
                    Extensions = { ["traceId"] = ctx.HttpContext.TraceIdentifier }
                }, ct);
            };

            static string ClientKey(HttpContext c) =>
            c.User.FindFirst("client_id")?.Value
            ?? c.User.FindFirst("azp")?.Value
            ?? c.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

            static RateLimitPartition<string> ByClient(HttpContext c, int limit) =>
            RateLimitPartition.GetFixedWindowLimiter(ClientKey(c), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });

            o.AddPolicy("create-payment", c => ByClient(c, 600));
            o.AddPolicy("payment-ops", c => ByClient(c, 300));
            o.AddPolicy("payment-read", c => ByClient(c, 1200));

            o.AddPolicy("callback", c =>
                RateLimitPartition.GetFixedWindowLimiter(
                    c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
    }
}
