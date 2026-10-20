using PaymentService.Domain.Enums;

namespace PaymentService.Application.Common.Exceptions;

public sealed class GatewayUnavailableException : AppException
{
    public GatewayProvider Provider { get; }
    public TimeSpan? RetryAfter { get; }

    public GatewayUnavailableException(GatewayProvider provider, string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        Provider = provider;
        RetryAfter = retryAfter;
    }
}