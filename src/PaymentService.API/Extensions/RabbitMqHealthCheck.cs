using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace PaymentService.API.Extensions;

public class RabbitMqHealthCheck:IHealthCheck
{
    private readonly IConnection _connection;
    public RabbitMqHealthCheck(IConnection connection)
    {
        _connection = connection;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => Task.FromResult(_connection.IsOpen ?
            HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("RabbitMQ connection is closed"));
}
