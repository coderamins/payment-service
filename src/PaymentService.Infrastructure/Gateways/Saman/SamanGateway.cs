using PaymentService.Infrastructure.Gateways.Helpers;
﻿using Azure;
using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using Serilog;

namespace PaymentService.Infrastructure.Gateways.Saman;

public class SamanGateway : IPaymentGateway
{
    private readonly SamanApiClient _apiClient;
    private readonly SamanOptions _options;
    private readonly ILogger<SamanGateway> _logger;
    private readonly IPaymentTransactionRepository _repository;

    public GatewayProvider Provider => GatewayProvider.Saman;

    public SamanGateway(SamanApiClient apiClient, SamanOptions options, ILogger<SamanGateway> logger, IPaymentTransactionRepository repository)
    {
        _apiClient = apiClient;
        _options = options;
        _logger = logger;
        _repository = repository;
    }

    public async Task<GatewayRequestResult> RequestPaymentAsync(
        PaymentTransaction transaction,
        string callbackUrl,
        string? payerNationalCode = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.RequestTokenAsync(
            transaction.PaymentTransactionId, 
            transaction.AmountInRials, 
            callbackUrl, 
            null/*TODO: Mobile*/, 
            cancellationToken);

        if (response.Status < 0)
        {
            _logger.LogWarning("RequestTokenAsync سامان برای سفارش {OrderId} ناموفق بود. Status={Status}",
                transaction.OrderId, response.Status);
            return new GatewayRequestResult(false, null, null, $"{response.ErrorCode} - {response.ErrorDesc}");
        }

        // کاربر باید با GET به این آدرس هدایت شود (نه POST)
        var redirectUrl = $"{_options.PaymentPageUrl.TrimEnd('/')}/?token={response.Token}";

        return new GatewayRequestResult(true, response.Token.ToString(), redirectUrl, null);
    }

    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> rawParameters)
    {
        /*
            rawParameters:
            ▪ MID : شماره ترمینال
            ▪ State : وضعیت تراکنش - حروف انگلیسی
            ▪ Status : وضعیت تراکنش - مقدار عددی
            ▪ RRN : شماره مرجع
            ▪ RefNum : رسید دیجیتالی خرید
            ▪ ResNum : شماره خرید
            ▪ TerminalId : شماره ترمینال
            ▪ TraceNo : شماره رهگیری 
            ▪ Amount
            ▪ Wage
            ▪ SecurePan : شماره کارتی که تراکنش با آن انجام شده است
            ▪ HashedCardNumber : شماره کارت هش شده SHA256
        */
        var state = GatewayParameterHelper.GetParameter(rawParameters, "State");
        var statusText = GatewayParameterHelper.GetParameter(rawParameters, "Status");
        var rrnText = GatewayParameterHelper.GetParameter(rawParameters, "RRN");
        var refNum = GatewayParameterHelper.GetParameter(rawParameters, "RefNum");
        var securePan = GatewayParameterHelper.GetParameter(rawParameters, "SecurePan");
        var amountRaw = GatewayParameterHelper.GetParameter(rawParameters, "Amount");

        long? reportedAmount = long.TryParse(amountRaw, out var amount) ? amount : null;

        var isSuccess = string.Equals(state, "OK", StringComparison.OrdinalIgnoreCase)
                && statusText == "2";

        int.TryParse(statusText, out var statusCode);
        var cleanRefNum = string.IsNullOrWhiteSpace(refNum) ? null : refNum.Trim();

        return new GatewayCallbackParseResult(
            isSuccess,
            statusText ?? "unknown",
            SaleReferenceId: isSuccess ? cleanRefNum : null,
            MaskedCardNumber: securePan,
            isSuccess ? null : MapCallbackError(statusCode),
            reportedAmount,
            null);
    }

    /// <summary>سامان Verify اولیه ندارد؛ Verify اصلی همان اقدام نهایی است و در SettleAsync انجام می‌شود.</summary>
    public Task<GatewayOperationResult> VerifyAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayOperationResult(true, "0", null));

    public async Task<GatewayOperationResult> SettleAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        var refNum = transaction.GatewaySaleReferenceId;
        if (string.IsNullOrWhiteSpace(refNum))
            return new GatewayOperationResult(false, "NO_SALE_REF", "شناسه مرجع سامان موجود نیست");

        // همین RefNum نباید به تراکنش دیگری هم نسبت داده شده باشد (جلوگیری از Replay)
        if (await _repository.ExistsOtherWithSaleReferenceAsync(
                Provider, refNum, transaction.PaymentTransactionId, cancellationToken))
        {
            _logger.LogCritical("RefNum سامان برای چند تراکنش تکرار شده. تراکنش {Id}", transaction.PaymentTransactionId);
            return new GatewayOperationResult(false, "REPLAY", "شناسه مرجع پرداخت تکراری است");
        }

        var response = await _apiClient.ConfirmAsync(refNum, _options.TerminalId, cancellationToken);
        var detail = response.TransactionDetail;

        // 0 موفق؛ 2 «درخواست تکراری» فقط وقتی پذیرفته می‌شود که جزئیات بانک در دسترس باشد و پایین چک شود
        var ok = response.ResultCode == 0 || (response.ResultCode == 2 && detail is not null);
        if (!ok)
            return new GatewayOperationResult(
                false, response.ResultCode.ToString(), MapResultCodeMessage(response.ResultCode));

        if (detail is null
            || detail.OrginalAmount != transaction.AmountInRials
            || (!string.IsNullOrEmpty(detail.RefNum) && detail.RefNum != refNum))
        {
            _logger.LogCritical(
                "مغایرت اطلاعات تأیید سامان. تراکنش {Id} مبلغ ما {Ours} مبلغ بانک {Theirs}",
                transaction.PaymentTransactionId, transaction.AmountInRials, detail?.OrginalAmount);

            // در سامان Verify همین مرحله‌ی نهایی است، پس Reverse همین‌جا درست است
            try { await _apiClient.ReversalAsync(refNum, _options.TerminalId, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Reverse بعد از مغایرت ناموفق بود: {Id}", transaction.PaymentTransactionId); }

            return new GatewayOperationResult(false, GatewayResultCodes.AmountMismatch, "مغایرت مبلغ با بانک");
        }

        return new GatewayOperationResult(true, response.ResultCode.ToString(), null, detail.MaskedPan);
    }


    public async Task<GatewayOperationResult> ReverseAsync(
        PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transaction.GatewaySaleReferenceId))
            return new GatewayOperationResult(false, "NO_SALE_REF", "شناسه مرجع سامان موجود نیست");

        var response = await _apiClient.ReversalAsync(
            transaction.GatewaySaleReferenceId, _options.TerminalId, cancellationToken);

        var ok = response.ResultCode == 0;
        return new GatewayOperationResult(
            ok, response.ResultCode.ToString(),
            ok ? null : MapResultCodeMessage(response.ResultCode),
            response.TransactionDetail?.MaskedPan);
    }

    private static string MapCallbackError(int statusCode) => statusCode switch
    {
        1 => "کاربر انصراف داده است",//CanceledByUser
        2 => "پرداخت با موفقیت انجام شد",//OK
        3 => "پرداخت انجام نشد",//Failed
        4 => "کاربر در بازه زمانی تعیین شده پاسخی ارسال نکرده است",//SessionIsNull
        5 => "پارامترهای ارسالی نامعتبر است",//InvalidParameters
        8 => "آدرس سرور پذیرنده نامعتبر است (در پرداخت های بر پایه توکن)",//MerchantIpAddressIsInvalid
        10 => "توکن ارسال شده یافت نشد",//TokenNotFound
        11 => "با این شماره ترمینال فقط تراکنش های توکنی قابل پرداخت هستند",//TokenRequired
        12 => "شماره ترمینال ارسال شده یافت نشد",//TerminalNotFound
        21 => "محدودیت های مدل چند حسابی رعایت نشده",//MultisettlePolicyErrors
        _ => $"وضعیت تراکنش نامشخص (کد {statusCode})"
    };

    private static string MapResultCodeMessage(int code) => code switch
    {
        -2 => "تراکنش یافت نشد",
        -6 => "بیش از نیم ساعت از زمان اجرای تراکنش گذشته است",
        0 => "عملیات با موفقیت انجام شد",
        2 => "درخواست تکراری می باشد",
        -104 => "ترمینال ارسالی غیرفعال می باشد",
        -105 => "ترمینال ارسالی در سیستم موجود نمی باشد",
        -106 => "آدرس آی پی درخواستی غیر مجاز می باشد",
        _ => $"تراکنش ناموفق بود (کد {code})"
    };
}
