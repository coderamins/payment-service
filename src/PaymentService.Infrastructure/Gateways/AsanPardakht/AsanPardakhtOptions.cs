namespace PaymentService.Infrastructure.Gateways.AsanPardakht;

public class AsanPardakhtOptions
{
    public const string SectionName = "PaymentGateways:AsanPardakht";

    public int MerchantConfigurationId { get; set; }

    public string UserName { get; set; } = default!;
    public string Password { get; set; } = default!;

    public string ApiBaseUrl { get; set; } = default!;

    /// <summary>صفحه‌ای که فرم RefId با POST به آن ارسال می‌شود</summary>
    public string PaymentPageUrl { get; set; } = "https://asan.shaparak.ir";

    public string EncryptionKey { get; set; } = default!;
    public string EncryptionIV { get; set; } = default!;

    public int TimeoutSeconds { get; set; } = 30;
}
