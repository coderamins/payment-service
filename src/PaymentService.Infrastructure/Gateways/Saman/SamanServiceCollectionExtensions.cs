using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Gateways.Helpers;

namespace PaymentService.Infrastructure.Gateways.Saman;

public static class SamanServiceCollectionExtensions
{
    public static IServiceCollection AddSamanGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SamanOptions>(configuration.GetSection(SamanOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SamanOptions>>().Value);

        services.AddHttpClient<SamanApiClient>()
            .UseGatewayPrimaryHandler(configuration);

        services.AddScoped<IPaymentGateway, SamanGateway>();

        return services;
    }
}