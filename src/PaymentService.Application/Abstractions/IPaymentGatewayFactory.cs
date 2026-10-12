using PaymentService.Domain.Enums;

namespace PaymentService.Application.Abstractions;

public interface IPaymentGatewayFactory
{
    IPaymentGateway Resolve(GatewayProvider provider);
}
