/* 
██████╗  █████╗ ███╗   ███╗██╗███╗   ██╗
██╔══██╗██╔══██╗████╗ ████║██║████╗  ██║
██████╔╝███████║██╔████╔██║██║██╔██╗ ██║
██╔══██╗██╔══██║██║╚██╔╝██║██║██║╚██╗██║
██║  ██║██║  ██║██║ ╚═╝ ██║██║██║ ╚████║
╚═╝  ╚═╝╚═╝  ╚═╝╚═╝     ╚═╝╚═╝╚═╝  ╚═══╝ */

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.API.Filters;
using PaymentService.Application.DTOs;
using PaymentService.Application.Services;
using PaymentService.Domain.Enums;

namespace PaymentService.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
//[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly PaymentOrchestrationService _orchestrationService;
    private readonly IConfiguration _configuration;

    public PaymentsController(PaymentOrchestrationService orchestrationService, IConfiguration configuration)
    {
        _orchestrationService = orchestrationService;
        _configuration = configuration;
    }

    /// <summary>ایجاد یک پرداخت جدید و دریافت آدرس هدایت کاربر به درگاه</summary>
    [HttpPost]
    [ServiceFilter(typeof(IdempotencyKeyFilter))]
    public async Task<ActionResult<CreatePaymentResult>> CreatePayment(
        [FromBody] CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orchestrationService
            .CreatePaymentAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result) : UnprocessableEntity(result);
    }

    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    [HttpPost("callback/{provider}/{callbackKey:guid}")]
    public async Task<IActionResult> GatewayCallback(
        string provider,
        Guid callbackKey,
        [FromForm] IFormCollection form,
        CancellationToken cancellationToken)
    {
        var rawParameters = form.ToDictionary(f => f.Key, f => f.Value.ToString());

        var result = await _orchestrationService.ConfirmCallbackAsync(
            new GatewayCallbackData(callbackKey, rawParameters), cancellationToken);

        var returnUrl= await _orchestrationService.
            GetReturnUrlAsync(result.TransactionId, cancellationToken);

        return Redirect(returnUrl);
    }

    /// <summary>
    /// تو مستندات پاسارگاد متد بازگشتی GET میباشد بخاطر همین این متد اضافه شد
    /// </summary>
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    [HttpGet("callback/pasargad/{callbackKey:guid}")]
    public Task<IActionResult> PasargadCallback(Guid callbackKey, CancellationToken cancellationToken)
    {
        var rawParameters = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());

        return HandleCallbackAsync(callbackKey, rawParameters, cancellationToken);
    }

    /// <summary>تسویه نهایی؛ بعد از تأیید شرایط کسب‌وکار توسط سرویس مصرف‌کننده</summary>
    [HttpPost("{transactionId:long}/settle")]
    [ServiceFilter(typeof(IdempotencyKeyFilter))]
    public async Task<ActionResult<ConfirmPaymentResult>> Settle(
        long transactionId, CancellationToken cancellationToken)
    {
        var result = await _orchestrationService
            .SettlePaymentAsync(transactionId, cancellationToken);

        return result.IsSuccess ? Ok(result) : UnprocessableEntity(result);
    }

    /// <summary>استعلام وضعیت با شناسه داخلی تراکنش</summary>
    [HttpGet("{transactionId:long}/status")]
    public async Task<ActionResult<PaymentStatusResult>> GetStatus(
        long transactionId,
        CancellationToken cancellationToken)
    {
        return Ok(await _orchestrationService
            .GetStatusAsync(transactionId, cancellationToken));
    }

    /// <summary>
    /// استعلام وضعیت تراکنش با شماره سفارش
    /// </summary>
    [HttpGet("by-order/{orderId}/status")]
    public async Task<ActionResult<PaymentStatusResult>> GetStatusByOrder(
        long orderId,
        EPaymentSubscriber paymentSubscriber,
        CancellationToken cancellationToken)
    {
        return Ok(await _orchestrationService
            .GetStatusByOrderIdAsync(orderId, paymentSubscriber, cancellationToken));
    }

    /// <summary>
    /// برگشت وجه
    /// </summary>
    [HttpPost("{transactionId:long}/reverse")]
    [ServiceFilter(typeof(IdempotencyKeyFilter))]
    public async Task<ActionResult<ConfirmPaymentResult>> Reverse(
        long transactionId,
        CancellationToken cancellationToken)
    {
        var result = await _orchestrationService
            .ReverseAsync(transactionId, cancellationToken);

        return result.IsSuccess ? Ok(result) : UnprocessableEntity(result);
    }


    private async Task<IActionResult> HandleCallbackAsync(
     Guid callbackKey,
     IDictionary<string, string> rawParameters,
     CancellationToken cancellationToken)
    {
        var result = await _orchestrationService.ConfirmCallbackAsync(
            new GatewayCallbackData(callbackKey, rawParameters), cancellationToken);

        var returnUrl = await _orchestrationService.
            GetReturnUrlAsync(result.TransactionId, cancellationToken);

        return Redirect(returnUrl);
    }
}
