using PaymentService.Domain.Enums;

namespace PaymentService.Application.Common;

public static class PaymentDisplayNames
{
    public static string Status(EPaymentStatus s) => s switch
    {
        EPaymentStatus.Created => "ایجاد شده",
        EPaymentStatus.RedirectedToGateway => "هدایت به درگاه",
        EPaymentStatus.CallbackReceived => "بازگشت از درگاه",
        EPaymentStatus.Verified => "تأیید شده",
        EPaymentStatus.Settled => "تسویه شده",
        EPaymentStatus.CanceledByUser => "لغو توسط کاربر",
        EPaymentStatus.Failed => "ناموفق",
        EPaymentStatus.Reversed => "برگشت خورده",
        EPaymentStatus.AmountMismatch => "مغایرت مبلغ",
        _ => s.ToString()
    };

    public static string Provider(GatewayProvider p) => p switch
    {
        GatewayProvider.Mellat => "به پرداخت ملت",
        GatewayProvider.AsanPardakht => "آسان‌پرداخت",
        GatewayProvider.Parsian => "پرداخت الکترونیک پارسیان",
        GatewayProvider.IranKish => "ایران کیش",
        GatewayProvider.Sadad => "بانک ملی(داده ورزی سداد)",
        GatewayProvider.Saman => "سامان کیش",
        GatewayProvider.Sepehr => "پرداخت الکترونیک سپهر",
        GatewayProvider.Pasargad=> "پرداخت الکترونیک پاسارگاد",
        GatewayProvider.PardakhtNovin=> "پرداخت نوین",

        _ => p.ToString()
    };

    public static PaymentOutcome Outcome(EPaymentStatus s) => s switch
    {
        EPaymentStatus.Settled => PaymentOutcome.Succeeded,

        EPaymentStatus.CanceledByUser or
        EPaymentStatus.Failed or
        EPaymentStatus.Reversed or
        EPaymentStatus.AmountMismatch => PaymentOutcome.Failed,

        _ => PaymentOutcome.Pending     // Created, RedirectedToGateway, CallbackReceived, Verified
    };
}