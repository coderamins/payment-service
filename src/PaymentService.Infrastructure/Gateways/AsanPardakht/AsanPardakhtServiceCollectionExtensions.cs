using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Gateways.Helpers;

namespace PaymentService.Infrastructure.Gateways.AsanPardakht;

public static class AsanPardakhtServiceCollectionExtensions
{
    public static IServiceCollection AddAsanPardakhtGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AsanPardakhtOptions>(configuration.GetSection(AsanPardakhtOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AsanPardakhtOptions>>().Value);

        services.AddHttpClient<AsanPardakhtApiClient>()
            .UseGatewayPrimaryHandler(configuration);

        services.AddScoped<IPaymentGateway, AsanPardakhtGateway>();

        return services;
    }
}