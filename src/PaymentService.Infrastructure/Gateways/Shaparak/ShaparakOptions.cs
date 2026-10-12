namespace PaymentService.Infrastructure.Gateways.Shaparak;

public class ShaparakOptions
{
    public const string SectionName = "PaymentGateways:ShaparakSettings";

    public string EncryptionKey { get; set; } = default!;
    public string EncryptionIV { get; set; } = default!;
}
