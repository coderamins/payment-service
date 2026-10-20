using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentService.Application.Abstractions;

namespace PaymentService.Infrastructure.Gateways.Health;

public static class GatewayHealthServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayHealth(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GatewayHealthOptions>(configuration.GetSection(GatewayHealthOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGatewayHealthMonitor, GatewayHealthMonitor>();
        return services;
    }
}