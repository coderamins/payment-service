using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Gateways.Helpers;

namespace PaymentService.Infrastructure.Gateways.Parsian;

public static class ParsianServiceCollectionExtensions
{
    public static IServiceCollection AddParsianGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ParsianOptions>(configuration.GetSection(ParsianOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ParsianOptions>>().Value);

        services.AddHttpClient<ParsianApiClient>()
            .UseGatewayPrimaryHandler(configuration);

        services.AddScoped<IPaymentGateway, ParsianGateway>();

        return services;
    }
}