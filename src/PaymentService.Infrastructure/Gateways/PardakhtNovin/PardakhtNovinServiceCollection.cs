using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;

namespace PaymentService.Infrastructure.Gateways.PardakhtNovin;

public static class PardakhtNovinServiceCollectionExtensions
{
    public static IServiceCollection AddPardakhtNovinGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PardakhtNovinOptions>(
            configuration.GetSection(PardakhtNovinOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PardakhtNovinOptions>>().Value);

        services.AddHttpClient<PardakhtNovinApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                if (configuration.GetValue<bool>("PaymentGateways:DisableSslValidationForLocalTestingOnly"))
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            });

        services.AddScoped<IPaymentGateway, PardakhtNovinGateway>();
        return services;
    }
}