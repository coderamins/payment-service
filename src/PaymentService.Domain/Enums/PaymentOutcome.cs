namespace PaymentService.Domain.Enums;

public enum PaymentOutcome
{
    Pending = 0,   // هنوز تمام نشده؛ فرانت «در حال بررسی» نشان دهد
    Succeeded = 1,   // پرداخت تسویه شده
    Failed = 2    // ناموفق یا لغو یا برگشت‌خورده
}
