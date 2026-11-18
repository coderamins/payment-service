namespace PaymentService.Domain.Enums;

/// <summary>
/// شناسه یکتای هر درگاه پرداخت 
/// </summary>
public enum GatewayProvider
{
    Mellat = 1,
    AsanPardakht = 2,
    Parsian = 3,
    Saman = 4,
    PardakhtNovin = 5,
    IranKish = 6,
    Pasargad = 7,
    Sepehr = 8,
    Sadad = 9,

    // نمونه درگاه‌های آینده :
    // Zarinpal = 4,
    // Melli = 5,
}
