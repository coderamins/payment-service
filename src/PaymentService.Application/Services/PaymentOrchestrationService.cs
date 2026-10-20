using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Common.Exceptions;
using PaymentService.Application.Common.Options;
using PaymentService.Application.DTOs;
using PaymentService.Application.Events;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using System.Text.Json;

namespace PaymentService.Application.Services;

public class PaymentOrchestrationService
{
    private readonly IPaymentGatewayFactory _gatewayFactory;
    private readonly IPaymentTransactionRepository _repository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly ILogger<PaymentOrchestrationService> _logger;
    private readonly PaymentCallbackOptions _callbackOptions;
    private readonly IGatewayHealthMonitor _healthMonitor;

    public PaymentOrchestrationService(
        IPaymentGatewayFactory gatewayFactory,
        IPaymentTransactionRepository repository,
        ILogger<PaymentOrchestrationService> logger,
        IOutboxWriter outboxWriter,
        PaymentCallbackOptions callbackOptions,
        IGatewayHealthMonitor healthMonitor)
    {
        _gatewayFactory = gatewayFactory;
        _repository = repository;
        _logger = logger;
        _outboxWriter = outboxWriter;
        _callbackOptions = callbackOptions;
        _healthMonitor = healthMonitor;
    }

    public async Task<CreatePaymentResult> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var provider = request.GatewayProvider;

        var gateway = _gatewayFactory.Resolve(provider);

        // اگر درگاه خارج از سرویس است، هیچ تراکنشی ساخته نمی‌شود
        var admission = _healthMonitor.TryAcquire(provider);
        if (!admission.Allowed)
            throw new GatewayUnavailableException(
                provider,
                admission.Reason ?? $"درگاه {provider} موقتاً در دسترس نیست",
                admission.RetryAfter);

        var transaction = PaymentTransaction.Create(
            request.OrderId,
            request.RequestingService,
            request.AmountInRials,
            request.ReturnUrl,
            provider);

        await _repository.AddAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        // ساخت آدرس callback بانک
        var callbackUrl = BuildCallbackUrl(transaction);

        GatewayRequestResult gatewayResult;

        try
        {
            gatewayResult = await gateway.RequestPaymentAsync(
                transaction,
                callbackUrl,
                request.PayerNationalCode,
                cancellationToken);
        }
        catch (GatewayCommunicationException ex)
        {
            _healthMonitor.RecordFailure(provider, ex.Message);

            transaction.MarkFailed(ex.Message);
            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(ex, "ارتباط با درگاه {Provider} برای سفارش {OrderId} برقرار نشد",
                provider, request.OrderId);

            // 503 و ذخیره نشدن در Idempotency: تلاش بعدی با همان کلید یک تراکنش تازه می‌سازد
            throw new GatewayUnavailableException(
                provider,
                $"ارتباط با درگاه {provider} برقرار نشد؛ دوباره تلاش کنید یا درگاه دیگری را انتخاب کنید");
        }

        // هر پاسخی از بانک (حتی خطای منطقی) یعنی درگاه زنده است
        _healthMonitor.RecordSuccess(provider);

        if (!gatewayResult.IsSuccess)
        {
            transaction.MarkFailed(gatewayResult.ErrorMessage ?? "خطای نامشخص در ارسال درخواست به درگاه");
            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "درخواست پرداخت به درگاه {Provider} برای سفارش {OrderId} ناموفق بود: {Error}",
                request.GatewayProvider, request.OrderId, gatewayResult.ErrorMessage);

            return new CreatePaymentResult(false, transaction.Id, null, gatewayResult.ErrorMessage);
        }

        transaction.MarkRedirectedToGateway(gatewayResult.RefId!);
        await _repository.UpdateAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreatePaymentResult(
            true, transaction.Id, gatewayResult.RedirectUrl, null,
            gatewayResult.RedirectMethod, gatewayResult.RedirectFormFields);
    }

    /// <summary>
    /// بازگشت از بانک: ثبت نتیجه + Verify. تسویه (Settle) اینجا انجام نمی‌شود؛
    /// سرویس مصرف‌کننده بعد از بررسی شرایط، settle یا reverse را صدا می‌زند.
    /// </summary>
    public async Task<ConfirmPaymentResult> ConfirmCallbackAsync(
        GatewayCallbackData callback,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _repository
            .GetByIdAsync(callback.TransactionId, cancellationToken)
            ?? throw new NotFoundException($"تراکنشی با شناسه {callback.TransactionId} یافت نشد");

        // callback تکراری (refresh یا ارسال مجدد بانک) نباید دوباره پردازش شود
        if (transaction.Status != PaymentStatus.RedirectedToGateway)
        {
            var paid = transaction.Status is PaymentStatus.Verified or PaymentStatus.Settled;
            return new ConfirmPaymentResult(
                paid, transaction.Id,
                paid ? transaction.GatewaySaleReferenceId : null,
                transaction.MaskedCardNumber,
                paid ? null : transaction.FailureReason ?? "این تراکنش قبلاً پردازش شده است");
        }

        var gateway = _gatewayFactory.Resolve(transaction.GatewayProvider);
        var parsed = gateway.ParseCallback(callback.RawParameters);

        transaction.MarkCallbackReceived(parsed.ResultCode, parsed.SaleReferenceId, parsed.MaskedCardNumber);

        if (!parsed.IsSuccessfulPayment)
        {
            transaction.MarkCanceledByUser();
            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return new ConfirmPaymentResult(
                false, transaction.Id, null, null,
                parsed.ErrorMessage ?? "پرداخت توسط کاربر لغو شد");
        }

        var verifyResult = await gateway.VerifyAsync(transaction, cancellationToken);

        if (!verifyResult.IsSuccess)
        {
            transaction.MarkFailed(verifyResult.ErrorMessage ?? "خطا در تایید تراکنش نزد بانک", verifyResult.ResultCode);
            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return new ConfirmPaymentResult(
                false, transaction.Id, null, transaction.MaskedCardNumber, verifyResult.ErrorMessage);
        }

        transaction.MarkVerified();   // پرداخت واقعی است؛ منتظر تصمیم سرویس مصرف‌کننده
        await _repository.UpdateAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new ConfirmPaymentResult(
            true, transaction.Id, transaction.GatewaySaleReferenceId, transaction.MaskedCardNumber, null);
    }

    /// <summary>
    /// تسویه نهایی؛ فقط بعد از اینکه سرویس مصرف‌کننده شرایط کسب‌وکار را تأیید کرد صدا زده می‌شود.
    /// </summary>
    public async Task<ConfirmPaymentResult> SettlePaymentAsync(
        long transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _repository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new NotFoundException($"تراکنشی با شناسه {transactionId} یافت نشد");

        // فراخوانی تکراری: همان نتیجه قبلی
        if (transaction.Status == PaymentStatus.Settled)
            return new ConfirmPaymentResult(true, transaction.Id,
                transaction.GatewaySaleReferenceId, transaction.MaskedCardNumber, null);

        if (transaction.Status != PaymentStatus.Verified)
            throw new ConflictException(
                $"تراکنش در وضعیت {transaction.Status} است و فقط تراکنش Verify شده قابل تسویه است");

        var gateway = _gatewayFactory.Resolve(transaction.GatewayProvider);
        var settleResult = await gateway.SettleAsync(transaction, cancellationToken);

        if (!settleResult.IsSuccess)
        {
            transaction.MarkFailed(settleResult.ErrorMessage ?? "خطا در تسویه تراکنش نزد بانک", settleResult.ResultCode);
            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return new ConfirmPaymentResult(
                false, transaction.Id, null, transaction.MaskedCardNumber, settleResult.ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(settleResult.MaskedCardNumber))
            transaction.SetMaskedCardNumber(settleResult.MaskedCardNumber);

        transaction.MarkSettled();
        _outboxWriter.Add("PaymentSettled", new PaymentSettledEvent(
            transaction.Id, transaction.OrderId, transaction.AmountInRials,
            transaction.GatewayProvider.ToString(), transaction.GatewaySaleReferenceId, DateTime.UtcNow));

        await _repository.UpdateAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "تراکنش {TransactionId} برای سفارش {OrderId} با موفقیت تسویه شد",
            transaction.Id, transaction.OrderId);

        return new ConfirmPaymentResult(
            true, transaction.Id, transaction.GatewaySaleReferenceId, transaction.MaskedCardNumber, null);
    }

    /// <summary>
    /// استعلام وضعیت یک تراکنش 
    /// </summary>
    public async Task<PaymentStatusResult> GetStatusAsync(
        long transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _repository
            .GetByIdAsync(transactionId, cancellationToken)
            ?? throw new NotFoundException($"تراکنشی با شناسه {transactionId} یافت نشد");

        return MapToStatusResult(transaction);
    }

    public async Task<PaymentStatusResult> GetStatusByOrderIdAsync(
        long orderId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _repository
            .GetByOrderIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"تراکنشی برای سفارش {orderId} یافت نشد");

        return MapToStatusResult(transaction);
    }

    /// <summary>
    /// عملیات بازگشت وجه
    /// </summary>
    public async Task<ConfirmPaymentResult> ReverseAsync(
        long transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _repository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new NotFoundException($"تراکنشی با شناسه {transactionId} یافت نشد");

        var gateway = _gatewayFactory.Resolve(transaction.GatewayProvider);
        var result = await gateway.ReverseAsync(transaction, cancellationToken);

        if (!result.IsSuccess)
            return new ConfirmPaymentResult(
                false, transaction.Id,
                null, transaction.MaskedCardNumber, result.ErrorMessage);

        transaction.MarkReversed();

        _outboxWriter.Add("PaymentReversed",
            new PaymentReversedEvent(
                transaction.Id,
                transaction.OrderId,
                transaction.AmountInRials,
                DateTime.UtcNow));

        await _repository.UpdateAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new ConfirmPaymentResult(true,
            transaction.Id,
            transaction.GatewaySaleReferenceId,
            transaction.MaskedCardNumber, null);
    }

    /// <summary>
    /// آدرسی که بانک بعد از پرداخت کاربر را به آن POST می‌کند.
    /// فرمت: {PublicBaseUrl}/api/v1/payments/callback/{provider}/{transactionId}
    /// (نام provider باید با segment مسیر در PaymentsController یکی باشد: mellat / asanpardakht).
    /// اینجا InvoiceId از طرف درگاه به URL اضافه میشه
    /// </summary>
    private string BuildCallbackUrl(PaymentTransaction transaction)
    {
        var baseUrl = _callbackOptions.PublicBaseUrl.TrimEnd('/');
        var provider = transaction.GatewayProvider.ToString().ToLowerInvariant();

        _logger.LogInformation($"Payment CallBack Url=>-------{baseUrl}/api/v1/payments/callback/{provider}/{transaction.Id}--------------");
        return $"{baseUrl}/api/v1/payments/callback/{provider}/{transaction.Id}";
    }

    private static PaymentStatusResult MapToStatusResult(PaymentTransaction t) => new(
        t.Id, t.OrderId, t.AmountInRials, t.Status.ToString(), t.GatewayProvider.ToString(),
        t.MaskedCardNumber, t.GatewaySaleReferenceId, t.FailureReason, t.CreatedAtUtc);

    public async Task<string> GetReturnUrlAsync(
        long transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _repository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new NotFoundException($"تراکنشی با شناسه {transactionId} یافت نشد");

        return transaction.ReturnUrl;
    }
}
