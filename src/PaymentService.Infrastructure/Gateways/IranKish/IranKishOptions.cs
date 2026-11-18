namespace PaymentService.Infrastructure.Gateways.IranKish;

public class IranKishOptions
{
    public const string SectionName = "PaymentGateways:IranKish";
    public string BaseUrl { get; set; } = "https://ikc.shaparak.ir";
    public string TerminalId { get; set; } = default!;
    public string AcceptorId { get; set; } = default!;
    public string PassPhrase { get; set; } = default!;
    public string RsaPubKeyBase64 { get; set; } = default!;
    public int TimeoutSeconds { get; set; } = 30;
}
