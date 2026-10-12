# Payment Service

سرویس مستقل پرداخت برای پلتفرم فروش اینترنتی خودرو. توسط دو سرویس مصرف می‌شود:
**وب‌سایت فروش** و **میز خدمت مشتری**. طراحی‌شده برای مقیاس بالا (~2M کاربر) و
افزودن آسان درگاه‌های پرداخت جدید بدون دست زدن به کد موجود.

## معماری (Clean Architecture + Strategy/Factory Pattern)

هر درگاه (ملت، سامان، پارسیان، زرین‌پال، ...) باید این اینترفیس را در
`PaymentService.Application/Abstractions/IPaymentGateway.cs` پیاده‌سازی کند:

- `RequestPaymentAsync` — ایجاد تراکنش نزد بانک و گرفتن لینک پرداخت
- `ParseCallback` — تفسیر پارامترهای بازگشتی از بانک (فرمت هر بانک فرق دارد)
- `VerifyAsync` / `SettleAsync` — تایید و تسویه نهایی
- `ReverseAsync` — برگشت وجه

## نحوه اضافه کردن یک درگاه جدید

1. یک مقدار به `GatewayProvider` در Domain اضافه کنید: `Saman = 2`
2. پوشه `Infrastructure/Gateways/Saman` بسازید و `SamanGateway : IPaymentGateway` را پیاده‌سازی کنید
   (دقیقاً مثل `MellatGateway` — کپی کردن ساختار همان پوشه سریع‌ترین راه است)
3. در `ServiceCollectionExtensions.AddPaymentInfrastructure` سه خط اضافه کنید:
   ```csharp
   services.Configure<SamanOptions>(configuration.GetSection(SamanOptions.SectionName));
   services.AddHttpClient<SamanSoapClient>();
   services.AddScoped<IPaymentGateway, SamanGateway>();
   ```

## جریان کامل یک پرداخت

1. `POST /api/payments` → ایجاد رکورد تراکنش + `bpPayRequest` → بازگشت `RedirectUrl`
2. کاربر در سایت بانک اطلاعات کارت را وارد می‌کند
3. بانک با `POST` به `/api/payments/callback/mellat/{transactionId}` نتیجه را می‌فرستد
4. سرویس: `ParseCallback` → `VerifyAsync` → `SettleAsync` → وضعیت نهایی `Settled`
5. سرویس مورد نظر مثلا میز خدمت مشتری با `GET /api/payments/{id}/status` یا
   `GET /api/payments/by-order/{orderId}/status` وضعیت را استعلام می‌کنند

