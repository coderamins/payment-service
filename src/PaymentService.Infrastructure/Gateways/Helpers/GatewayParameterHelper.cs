namespace PaymentService.Infrastructure.Gateways.Helpers;

public static class GatewayParameterHelper
{
    public static string? GetParameter(IDictionary<string, string> parameters, string name)
    {
        foreach (var (key, value) in parameters)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }
}
