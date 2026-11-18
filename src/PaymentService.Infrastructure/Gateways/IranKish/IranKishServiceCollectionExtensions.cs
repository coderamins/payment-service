using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Gateways.Helpers;

namespace PaymentService.Infrastructure.Gateways.IranKish;

public static class IranKishServiceCollectionExtensions
{
    public static IServiceCollection AddIranKishGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IranKishOptions>(configuration.GetSection(IranKishOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<IranKishOptions>>().Value);
        services.AddHttpClient<IranKishApiClient>().UseGatewayPrimaryHandler(configuration);
        services.AddScoped<IPaymentGateway, IranKishGateway>();
        return services;
    }
}
