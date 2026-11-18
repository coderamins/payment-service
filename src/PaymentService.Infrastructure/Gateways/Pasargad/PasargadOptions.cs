namespace PaymentService.Infrastructure.Gateways.Pasargad;

public class PasargadOptions
{
    public const string SectionName = "PaymentGateways:Pasargad";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int TerminalNumber { get; set; }

    public string ApiBaseUrl { get; set; } = string.Empty;

    public string? Referer { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public int DefaultTokenLifetimeSeconds { get; set; } = 600;

    public int TokenRefreshMarginSeconds { get; set; } = 60;
}
