using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Common.Options;
using PaymentService.Application.Services;
using PaymentService.Infrastructure.Gateways;
using PaymentService.Infrastructure.Gateways.AsanPardakht;
using PaymentService.Infrastructure.Gateways.Mellat;
using PaymentService.Infrastructure.Gateways.Shaparak;
using PaymentService.Infrastructure.Messaging;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("PaymentDb")));

        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();

        // --- تنظیمات ساخت آدرس Callback بانک (مقدار اشتباه همان ابتدای اجرا خطا می‌دهد) ---
        var callbackOptions = configuration
            .GetSection(PaymentCallbackOptions.SectionName)
            .Get<PaymentCallbackOptions>() ?? new PaymentCallbackOptions();

        if (!Uri.TryCreate(callbackOptions.PublicBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException(
                $"تنظیم '{PaymentCallbackOptions.SectionName}:{nameof(PaymentCallbackOptions.PublicBaseUrl)}' " +
                "باید یک آدرس مطلق معتبر باشد (مثلاً https://api.example.com)");

        services.AddSingleton(callbackOptions);

        // --- سرویس هماهنگ‌کننده اصلی ---
        services.AddScoped<PaymentOrchestrationService>();

        // --- Factory: خودکار همه IPaymentGateway های ثبت‌شده زیر را جمع می‌کند ---
        services.AddScoped<IPaymentGatewayFactory, PaymentGatewayFactory>();

        // ===================================================================
        // ثبت درگاه ملت
        // ===================================================================
        services.Configure<MellatOptions>(configuration.GetSection(MellatOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MellatOptions>>().Value);

        services.AddHttpClient<MellatSoapClient>()
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandlerFactory(configuration));

        services.AddScoped<IPaymentGateway, MellatGateway>();

        // ===================================================================
        // ثبت درگاه آسان پرداخت
        // ===================================================================
        services.Configure<AsanPardakhtOptions>(configuration.GetSection(AsanPardakhtOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AsanPardakhtOptions>>().Value);

        services.AddHttpClient<AsanPardakhtApiClient>()
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandlerFactory(configuration));

        services.AddScoped<IPaymentGateway, AsanPardakhtGateway>();

        // ===================================================================
        // تنظیمات شاپرک
        services.Configure<ShaparakOptions>(configuration.GetSection(ShaparakOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ShaparakOptions>>().Value);
        // ===================================================================
        // RabbitMQ Publisher
        // ===================================================================
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value);
        services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();

        // --- Worker پردازش Outbox ---
        services.AddHostedService<OutboxProcessorBackgroundService>();

        return services;
    }

    private static Func<HttpMessageHandler> CreatePrimaryHandlerFactory(IConfiguration configuration)
    {
        return () =>
        {
            var handler = new HttpClientHandler();

            // فقط برای تست لوکال! این پرچم هرگز نباید در appsettings.json اصلی یا appsettings.Production.json ست بشه.
            if (configuration.GetValue<bool>("PaymentGateways:DisableSslValidationForLocalTestingOnly"))
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            return handler;
        };
    }
}