using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Gateways.Helpers;
using PaymentService.Infrastructure.Workers;

namespace PaymentService.Infrastructure.Gateways.AsanPardakht;

public static class PaymentWorkerServiceExtensions
{
    public static IServiceCollection AddPaymentServiceWorkers(
        this IServiceCollection services)
    {
        services.AddHostedService<PaymentReconciliationWorker>();
        

        return services;
    }
}