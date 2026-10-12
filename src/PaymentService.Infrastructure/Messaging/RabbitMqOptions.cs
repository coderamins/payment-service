namespace PaymentService.Infrastructure.Messaging;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>نام Exchange که همه‌ی رویدادهای پرداخت روی آن منتشر می‌شوند</summary>
    public string ExchangeName { get; set; } = "payment.events";
}