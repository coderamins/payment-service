using PaymentService.Application.Abstractions;
using PaymentService.Application.Common.Exceptions;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Gateways;

public class PaymentGatewayFactory : IPaymentGatewayFactory
{
    private readonly IReadOnlyDictionary<GatewayProvider, IPaymentGateway> _gateways;

    public PaymentGatewayFactory(IEnumerable<IPaymentGateway> gateways)
    {
        _gateways = gateways.ToDictionary(g => g.Provider);
    }

    public IPaymentGateway Resolve(GatewayProvider provider)
    {
        if (_gateways.TryGetValue(provider, out var gateway))
            return gateway;

        throw new UnsupportedGatewayException(
            $"درگاه پرداخت '{provider}' در سیستم پیاده‌سازی یا در DI ثبت نشده است.");
    }
}
