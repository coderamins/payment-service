using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Gateways.Helpers;

namespace PaymentService.Infrastructure.Gateways.Mellat;

public static class MellatServiceCollectionExtensions
{
    public static IServiceCollection AddMellatGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MellatOptions>(configuration.GetSection(MellatOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MellatOptions>>().Value);

        services.AddHttpClient<MellatSoapClient>()
            .UseGatewayPrimaryHandler(configuration);

        services.AddScoped<IPaymentGateway, MellatGateway>();

        return services;
    }
}