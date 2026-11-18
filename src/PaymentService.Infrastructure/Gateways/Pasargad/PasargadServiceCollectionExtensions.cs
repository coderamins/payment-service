using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Gateways.Helpers;

namespace PaymentService.Infrastructure.Gateways.Pasargad;

public static class PasargadServiceCollectionExtensions
{
    public static IServiceCollection AddPasargadGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PasargadOptions>(configuration.GetSection(PasargadOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PasargadOptions>>().Value);

        // کش توکن باید Singleton باشد (HttpClient تایپ‌شده Transient است)
        services.AddSingleton<PasargadTokenCache>();

        services.AddHttpClient<PasargadApiClient>()
            .UseGatewayPrimaryHandler(configuration);

        services.AddScoped<IPaymentGateway, PasargadGateway>();

        return services;
    }
}
