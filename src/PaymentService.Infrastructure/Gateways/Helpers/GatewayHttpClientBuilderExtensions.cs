using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentService.Infrastructure.Gateways.Helpers;

internal static class GatewayHttpClientBuilderExtensions
{
    /// <summary>
    /// تنظیم handler اصلی HttpClient درگاه‌ها.
    /// فقط برای تست لوکال! این پرچم هرگز نباید در appsettings.json اصلی یا Production ست شود.
    /// </summary>
    public static IHttpClientBuilder UseGatewayPrimaryHandler(
        this IHttpClientBuilder builder, IConfiguration configuration)
        => builder.ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler();

            if (configuration.GetValue<bool>("PaymentGateways:DisableSslValidationForLocalTestingOnly"))
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            return handler;
        });
}