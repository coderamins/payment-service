using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Common;
using PaymentService.Application.Common.Exceptions;
using PaymentService.Application.Common.Options;
using PaymentService.Application.DTOs;
using PaymentService.Application.Events;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using System.Data;

namespace PaymentService.Application.Services;

public class PaymentOrchestrationService
{
    private readonly IPaymentGatewayFactory _gatewayFactory;
    private readonly IPaymentTransactionRepository _repository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IGatewayHealthMonitor _healthMonitor;
    private readonly ILogger<PaymentOrchestrationService> _logger;
    private readonly PaymentCallbackOptions _callbackOptions;

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
        //if (!ReturnUrlValidator.IsAllowed(request.ReturnUrl, _callbackOptions))
        //    throw new ValidationException(new Dictionary<string, string[]>
        //    {
        //        [nameof(request.ReturnUrl)] = new[] { "دامنه‌ی آدرس بازگشت مجاز نیست" }
        //    });

        var hasSplits = request.Splits is { Count: > 0 };

        //if ((hasSplits || request.TerminalNumber is not null)
        //    && request.GatewayProvider != GatewayProvider.Pasargad)
        //    throw new ValidationException(new Dictionary<string, string[]>
        //    {
        //        ["Splits"] = new[] { "تعیین ترمینال و تسهیم داینامیک فعلاً فقط برای درگاه پاسارگاد پشتیبانی می‌شود" }
        //    });

        if (request.TerminalNumber is <= 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.TerminalNumber)] = new[] { "شماره ترمینال نامعتبر است" }
            });

        var splitShares = hasSplits
            ? PaymentSplitCalculator.Normalize(request.AmountInRials, request.SplitType, request.Splits!)
            : null;

        var gateway = _gatewayFactory
            .Resolve(request.GatewayProvider);

        var activePays = await _repository
            .GetActivePayments(
                request.OrderId,
                request.PaymentSubscriber,
                cancellationToken);

        if (activePays.Any())
            throw new ConflictException("برای این سفارش پرداخت در حال بررسی یا انجام‌شده وجود دارد");

        var _acuire=_healthMonitor.TryAcquire(request.GatewayProvider);
        if(!_acuire.Allowed)
            throw new GatewayUnavailableException(
                $"این درگاه در حال حاضر در دسترس نمی باشد. لطفا از درگاه دیگری استفاده کنید یا بعدا تلاش کنید.");

        var transaction = PaymentTransaction.Create(
            request.OrderId,
            request.PaymentSubscriber,
            request.AmountInRials,
            request.ReturnUrl,
            request.GatewayProvider,
            request.TerminalNumber,
            splitShares);

        await _repository.AddAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        // ساخت آدرس callback بانک
        var callbackUrl = BuildCallbackUrl(transaction);

        try
        {
            var gatewayResult = await gateway.RequestPaymentAsync(
                transaction,
                callbackUrl,
                request.PayerNationalCode,
                cancellationToken);

            if (!gatewayResult.IsSuccess)
            {
                transaction.MarkFailed(gatewayResult.ErrorMessage ?? "خطای نامشخص در ارسال درخواست به درگاه");
                await _repository.UpdateAsync(transaction, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "درخواست پرداخت به درگاه {Provider} برای سفارش {OrderId} ناموفق بود: {Error}",
                    request.GatewayProvider, request.OrderId, gatewayResult.ErrorMessage);

                _healthMonitor.RecordFailure(gateway.Provider, "خطای نامشخص در ارسال درخواست به درگاه");

                return new CreatePaymentResult(
                    false,
                    transaction.PaymentTransactionId,
                    null,
                    gatewayResult.ErrorMessage);
            }

            transaction.MarkRedirectedToGateway(gatewayResult.RefId!);
            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _healthMonitor.RecordSuccess(gateway.Provider);
            return new CreatePaymentResult(
                true,
                transaction.PaymentTransactionId,
                gatewayResult.RedirectUrl,
                null,
                gatewayResult.RedirectMethod,
                gatewayResult.RedirectFormFields);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "ارتباط با درگاه برای تراکنش {Id} ناموفق بود", transaction.PaymentTransactionId);

            transaction.MarkFailed("GatewayUnavailable");
            await _repository.UpdateAsync(transaction, CancellationToken.None);
            await _repository.SaveChangesAsync(CancellationToken.None);

            _healthMonitor.RecordFailure(gateway.Provider, "GatewayUnavailable");
            throw new GatewayUnavailableException($"ارتباط با درگاه برای تراکنش {transaction.PaymentTransactionId} ناموفق بود");
        }
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
            .GetByCallbackKeyAsync(callback.CallbackKey, cancellationToken)
            ?? throw new NotFoundException($"تراکنش یافت نشد");

        // callback تکراری (refresh یا ارسال مجدد بانک) نباید دوباره پردازش شود
        if (transaction.PaymentStatusId != EPaymentStatus.RedirectedToGateway)
        {
            var alreadyPaid = transaction.PaymentStatusId is EPaymentStatus.Verified or EPaymentStatus.Settled;
            return new ConfirmPaymentResult(
                alreadyPaid, transaction.PaymentTransactionId,
                alreadyPaid ? transaction.GatewaySaleReferenceId : null,
                transaction.MaskedCardNumber,
                alreadyPaid ? null : transaction.FailureReason ?? "این تراکنش قبلاً پردازش شده است");
        }

        var gateway = _gatewayFactory.Resolve(transaction.GatewayProviderId);
        var parsed = gateway.ParseCallback(callback.RawParameters);

        if (parsed.CallbackToken is not null &&
            !string.Equals(parsed.CallbackToken, transaction.GatewayRefId, StringComparison.Ordinal))
        {
            _logger.LogWarning("توکن callback با توکن ذخیره‌شده نمی‌خواند. تراکنش {Id}", transaction.PaymentTransactionId);
            return new ConfirmPaymentResult(false, transaction.PaymentTransactionId, null, null, "callback نامعتبر است.");
        }

        transaction.MarkCallbackReceived(parsed.ResultCode, parsed.SaleReferenceId, parsed.MaskedCardNumber);

        #region کنترل concurrency
        // ادعای پردازش: اگر درخواست دیگری هم‌زمان همین تراکنش را برداشته باشد، اینجا شکست می‌خورد
        transaction.Touch();
        try
        {
            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            var current = await _repository.GetByCallbackKeyAsync(callback.CallbackKey, cancellationToken)
                ?? throw new NotFoundException("تراکنش یافت نشد");

            var paidNow = current.PaymentStatusId is EPaymentStatus.Verified or EPaymentStatus.Settled;
            return new ConfirmPaymentResult(
                paidNow,
                current.PaymentTransactionId,
                paidNow ? current.GatewaySaleReferenceId : null,
                current.MaskedCardNumber,
                paidNow ? null : current.FailureReason ?? "این تراکنش در حال پردازش می باشد .");
        }
        #endregion

        var paid = parsed.IsSuccessfulPayment;
        GatewayOperationResult? verifyResult = null;

        // برخی درگاه‌ها (ملت) حتی با نتیجه‌ی ناموفق باید Verify شوند تا وضعیت واقعی معلوم شود
        if (!paid && parsed.VerifyEvenIfUnsuccessful &&
            !string.IsNullOrWhiteSpace(transaction.GatewaySaleReferenceId))
        {
            verifyResult = await gateway.VerifyAsync(transaction, cancellationToken);
            if (verifyResult.IsSuccess)
            {
                paid = true;
                _logger.LogWarning(
                    "callback ناموفق بود ولی بانک Verify را تأیید کرد. تراکنش {TransactionId}",
                    transaction.PaymentTransactionId);
            }
        }

        if (!paid)
        {
            transaction.MarkCanceledByUser();
            _outboxWriter.Add("PaymentCanceled", new PaymentCanceledEvent(
                transaction.PaymentTransactionId, transaction.OrderId, transaction.AmountInRials,
                transaction.GatewayProviderId.ToString(), DateTime.UtcNow));

            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return new ConfirmPaymentResult(
                false, transaction.PaymentTransactionId, null, null,
                parsed.ErrorMessage ?? "پرداخت توسط کاربر لغو شد");
        }

        if (parsed.IsSuccessfulPayment
            && parsed.ReportedAmountInRials is decimal reported
            && reported != transaction.AmountInRials)
        {
            transaction.MarkAmountMismatch();
            _outboxWriter.Add("AmountMismatch", new PaymentAmountMismatchEvent(
                transaction.PaymentTransactionId, transaction.OrderId, transaction.AmountInRials,
                parsed.ReportedAmountInRials ?? 0,
                transaction.GatewayProviderId.ToString(), DateTime.UtcNow));

            try
            {
                var rev = await gateway.ReverseAsync(transaction, cancellationToken);
                if (rev.IsSuccess) transaction.MarkReversed();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reverse بعد از مغایرت مبلغ ناموفق بود: {Id}", transaction.PaymentTransactionId);
            }

            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return new ConfirmPaymentResult(
                false, transaction.PaymentTransactionId, null, null,
                parsed.ErrorMessage ?? "مغایرت مبلغ سفارش با مبلغ برگشتی از بانک");
        }
        else if (parsed.IsSuccessfulPayment && parsed.ReportedAmountInRials is null)
            _logger.LogWarning("بانک مبلغ تراکنش {Id} را در callback نفرستاد؛ مقایسه انجام نشد", transaction.PaymentTransactionId);

        verifyResult ??= await gateway.VerifyAsync(transaction, cancellationToken);

        if (!verifyResult.IsSuccess)
        {
            transaction.MarkFailed(verifyResult.ErrorMessage ?? "خطا در تایید تراکنش نزد بانک", verifyResult.ResultCode);
            _outboxWriter.Add("PaymentFailed", new PaymentFailedEvent(
                transaction.PaymentTransactionId, transaction.OrderId, transaction.AmountInRials,
                transaction.GatewayProviderId.ToString(), "Verify", transaction.FailureReason, DateTime.UtcNow));

            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return new ConfirmPaymentResult(
                false, transaction.PaymentTransactionId, null, transaction.MaskedCardNumber, verifyResult.ErrorMessage);
        }

        // برخی درگاه‌ها (مثل پاسارگاد) کارت ماسک‌شده را در Verify برمی‌گردانند نه در callback
        if (!string.IsNullOrWhiteSpace(verifyResult.MaskedCardNumber))
            transaction.SetMaskedCardNumber(verifyResult.MaskedCardNumber);

        transaction.MarkVerified();   // پرداخت واقعی است؛ منتظر تصمیم سرویس مصرف‌کننده
        _outboxWriter.Add("PaymentVerified", new PaymentVerifiedEvent(
            transaction.PaymentTransactionId, transaction.OrderId, transaction.AmountInRials,
            transaction.GatewayProviderId.ToString(), transaction.GatewaySaleReferenceId, DateTime.UtcNow));

        await _repository.UpdateAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new ConfirmPaymentResult(
            true, transaction.PaymentTransactionId, transaction.GatewaySaleReferenceId, transaction.MaskedCardNumber, null);
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
        if (transaction.PaymentStatusId == EPaymentStatus.Settled)
            return new ConfirmPaymentResult(true, transaction.PaymentTransactionId,
                transaction.GatewaySaleReferenceId, transaction.MaskedCardNumber, null);

        if (transaction.PaymentStatusId != EPaymentStatus.Verified)
            throw new ConflictException(
                $"تراکنش در وضعیت {transaction.PaymentStatusId} است و فقط تراکنش Verify شده قابل تسویه است");

        var gateway = _gatewayFactory.Resolve(transaction.GatewayProviderId);

        transaction.Touch();
        try
        {
            await _repository.UpdateAsync(transaction,cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch(ConcurrencyException)
        {
            throw new ConflictException("این تراکنش در حال پردازش دیگری است.");
        }

        var settleResult = await gateway.SettleAsync(transaction, cancellationToken);

        if (!settleResult.IsSuccess)
        {
            transaction.MarkFailed(settleResult.ErrorMessage ?? "خطا در تسویه تراکنش نزد بانک", settleResult.ResultCode);
            await _repository.UpdateAsync(transaction, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return new ConfirmPaymentResult(
                false, transaction.PaymentTransactionId, null, transaction.MaskedCardNumber, settleResult.ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(settleResult.MaskedCardNumber))
            transaction.SetMaskedCardNumber(settleResult.MaskedCardNumber);

        transaction.MarkSettled();
        _outboxWriter.Add("PaymentSettled", new PaymentSettledEvent(
            transaction.PaymentTransactionId, transaction.OrderId, transaction.AmountInRials,
            transaction.GatewayProviderId.ToString(), transaction.GatewaySaleReferenceId, DateTime.UtcNow));

        await _repository.UpdateAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "تراکنش {TransactionId} برای سفارش {OrderId} با موفقیت تسویه شد",
            transaction.PaymentTransactionId, transaction.OrderId);

        return new ConfirmPaymentResult(
            true, transaction.PaymentTransactionId, transaction.GatewaySaleReferenceId, transaction.MaskedCardNumber, null);
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
        EPaymentSubscriber paymentSubscriber,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _repository
            .GetByOrderIdAsync(orderId,
                paymentSubscriber,
                cancellationToken)
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

        var gateway = _gatewayFactory.Resolve(transaction.GatewayProviderId);
        var result = await gateway.ReverseAsync(transaction, cancellationToken);

        if (!result.IsSuccess)
            return new ConfirmPaymentResult(
                false, transaction.PaymentTransactionId,
                null, transaction.MaskedCardNumber, result.ErrorMessage);

        transaction.MarkReversed();

        _outboxWriter.Add("PaymentReversed",
            new PaymentReversedEvent(
                transaction.PaymentTransactionId,
                transaction.OrderId,
                transaction.AmountInRials,
                DateTime.UtcNow));

        await _repository.UpdateAsync(transaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new ConfirmPaymentResult(true,
            transaction.PaymentTransactionId,
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
        var provider = transaction.GatewayProviderId.ToString().ToLowerInvariant();

        return $"{baseUrl}/api/v1/payments/callback/{provider}/{transaction.CallbackKey}";
    }

    private static PaymentStatusResult MapToStatusResult(PaymentTransaction t) => new(
        t.PaymentTransactionId, 
        t.OrderId, 
        t.AmountInRials,
        PaymentDisplayNames.Outcome(t.PaymentStatusId), 
        t.GatewayProviderId,
        t.MaskedCardNumber, 
        t.GatewaySaleReferenceId, 
        t.GatewayRefId, 
        t.FailureReason, 
        PaymentDisplayNames.Status(t.PaymentStatusId),
        PaymentDisplayNames.Provider(t.GatewayProviderId),        
        t.CreatedAt, 
        t.ModifiedAt);

    public async Task<string> GetReturnUrlAsync(
    long transactionId,
    CancellationToken cancellationToken = default)
    {
        var transaction = await _repository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new NotFoundException($"تراکنشی با شناسه {transactionId} یافت نشد");

        return (transaction.ReturnUrl);
    }
}
