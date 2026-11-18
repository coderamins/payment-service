namespace PaymentService.Infrastructure.Gateways.Sedad;

public class MelliOptions
{
    public const string SectionName = "PaymentGateways:Melli";

    public string MerchantId { get; set; } = default!;
    public string TerminalId { get; set; } = default!;
    public string TerminalKey { get; set; } = default!;

    public string ApiBaseUrl { get; set; } = "https://sadad.shaparak.ir";
    public string PaymentPageUrl { get; set; } = "https://sadad.shaparak.ir/Purchase";

    /// <summary>
    /// فقط برای ترمینال «تسهیم از سمت پذیرنده»: شبایی که پرداخت‌های بدون تسهیم با کل مبلغ به آن می‌رود.
    /// </summary>
    public string? DefaultSheba { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}
