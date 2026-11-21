using PaymentService.Domain.Enums;

namespace PaymentService.Application.Abstractions;

public enum GatewayErrorOperation
{
    Request,
    Verify,
    Settle,
    Reverse,
    Callback
}

public interface IGatewayErrorMapper
{
    GatewayProvider Provider { get; }
    string MapError(object resultCode, GatewayErrorOperation operation, string? bankMessage = null);
}
