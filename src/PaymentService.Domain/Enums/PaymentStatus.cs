namespace PaymentService.Domain.Enums;

/// <summary>
/// وضعیت کلی یک تراکنش پرداخت در طول چرخه حیاتش
/// </summary>
public enum PaymentStatus
{
    /// <summary>تراکنش ایجاد شده ولی هنوز به درگاه ارسال نشده</summary>
    Created = 0,

    /// <summary>درخواست به درگاه ارسال شد و کاربر به صفحه بانک هدایت شد</summary>
    RedirectedToGateway = 1,

    /// <summary>بانک نتیجه پرداخت را برگردانده (Callback دریافت شد) - در انتظار Verify</summary>
    CallbackReceived = 2,

    /// <summary>پرداخت نزد بانک تأیید شده ولی هنوز Settle نشده؛ منتظر تصمیم سرویس مصرف‌کننده (settle یا reverse)</summary>
    Verified = 3,

    /// <summary>تراکنش با موفقیت کامل تسویه شده - پایان موفق چرخه</summary>
    Settled = 4,

    /// <summary>کاربر پرداخت را لغو کرده یا از صفحه بانک برگشته</summary>
    CanceledByUser = 5,

    /// <summary>پرداخت توسط بانک رد شده یا در Verify/Settle خطا رخ داده</summary>
    Failed = 6,

    /// <summary>مبلغ به کاربر بازگشت داده شده (Refund/Reverse)</summary>
    Reversed = 7
}
