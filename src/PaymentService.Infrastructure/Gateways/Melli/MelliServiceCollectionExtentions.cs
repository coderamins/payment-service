using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Gateways.Sedad;

namespace PaymentService.Infrastructure.Gateways.Melli;

public static class MelliServiceCollectionExtensions
{
    public static IServiceCollection AddMelliGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MelliOptions>(configuration.GetSection(MelliOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MelliOptions>>().Value);

        services.AddHttpClient<MelliApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                if (configuration.GetValue<bool>("PaymentGateways:DisableSslValidationForLocalTestingOnly"))
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            });

        services.AddScoped<IPaymentGateway, MelliGateway>();
        return services;
    }
}