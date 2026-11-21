using PaymentService.Application.Abstractions;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Gateways.Mapping;

public abstract class GatewayErrorMapperBase : IGatewayErrorMapper
{
    private readonly IReadOnlyDictionary<GatewayErrorOperation, IReadOnlyDictionary<object, string>> _errorMaps;

    protected GatewayErrorMapperBase(IReadOnlyDictionary<GatewayErrorOperation, IReadOnlyDictionary<object, string>> errorMaps)
    {
        _errorMaps = errorMaps;
    }

    public abstract GatewayProvider Provider { get; }

    public string MapError(object resultCode, GatewayErrorOperation operation, string? bankMessage = null)
    {
        if (_errorMaps.TryGetValue(operation, out var map) && map.TryGetValue(resultCode, out var message))
            return message;

        var fallback = GetFallbackMessage(resultCode, operation, bankMessage);
        if (fallback is not null)
            return fallback;

        if (!string.IsNullOrWhiteSpace(bankMessage))
            return $"{bankMessage} (کد {resultCode})";

        return $"خطای نامشخص (کد {resultCode})";
    }

    protected virtual string? GetFallbackMessage(object resultCode, GatewayErrorOperation operation, string? bankMessage) => null;
}
