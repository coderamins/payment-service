namespace PaymentService.Infrastructure.Gateways.PardakhtNovin;

public class PardakhtNovinOptions
{
    public const string SectionName = "PaymentGateways:PardakhtNovin";

    public string CorporationPin { get; set; } = default!;
    public string ApiBaseUrl { get; set; } = "https://pna.shaparak.ir";
    public string PaymentPageUrl { get; set; } = "https://pna.shaparak.ir/mhui/home/index";
    public int TimeoutSeconds { get; set; } = 30;
}
