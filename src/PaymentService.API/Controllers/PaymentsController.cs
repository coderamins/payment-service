/*██████╗  █████╗ ███╗   ███╗██╗███╗   ██╗
  ██╔══██╗██╔══██╗████╗ ████║██║████╗  ██║
  ██████╔╝███████║██╔████╔██║██║██╔██╗ ██║
  ██╔══██╗██╔══██║██║╚██╔╝██║██║██║╚██╗██║
  ██║  ██║██║  ██║██║ ╚═╝ ██║██║██║ ╚████║
  ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝     ╚═╝╚═╝╚═╝  ╚═══╝*/
using Microsoft.AspNetCore.Mvc;
using PaymentService.API.Filters;
using PaymentService.Application.DTOs;
using PaymentService.Application.Common.Exceptions;
using PaymentService.Application.Services;
using Asp.Versioning;

namespace PaymentService.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
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

    /// <summary>بازگشت کاربر از هر درگاه؛ نوع درگاه از روی خود تراکنش تشخیص داده می‌شود</summary>
    [HttpPost("callback/{provider}/{transactionId:long}")]
    public async Task<IActionResult> GatewayCallback(
        string provider,
        long transactionId,
        [FromForm] IFormCollection form,
        CancellationToken cancellationToken)
    {
        var rawParameters = form.ToDictionary(f => f.Key, f => f.Value.ToString());

        var result = await _orchestrationService.HandleGatewayCallbackAsync(
            new GatewayCallbackData(transactionId, rawParameters), cancellationToken);

        var returnUrl = await _orchestrationService.GetReturnUrlAsync(transactionId, cancellationToken);
        return Redirect($"{returnUrl}?transactionId={transactionId}&paid={result.IsPaid}");
    }

    ///// <summary>
    ///// وقتی نتیجه پرداخت برگشت کاربر به صفحه نتیجه پرداخت که در تنظیمات ست کردیم هدایت میشه
    ///// </summary>
    ///// <returns></returns>
    //[HttpPost("callback/mellat/{transactionId:guid}")]
    //public async Task<IActionResult> MellatCallback(
    //long transactionId, [FromForm] IFormCollection form, 
    //CancellationToken cancellationToken)
    //{
    //    var rawParameters = form.ToDictionary(f => f.Key, f => f.Value.ToString());

    //    var result = await _orchestrationService.HandleGatewayCallbackAsync(
    //        new GatewayCallbackData(transactionId, rawParameters), cancellationToken);

    //    var returnUrl = await _orchestrationService.GetReturnUrlAsync(transactionId, cancellationToken);
    //    return Redirect($"{returnUrl}?transactionId={transactionId}&paid={result.IsPaid}");
    //}

    //[HttpPost("callback/asanpardakht/{transactionId:long}")]
    //public async Task<IActionResult> AsanPardakhtCallback(
    //long transactionId,
    //[FromForm] IFormCollection form,
    //CancellationToken cancellationToken)
    //{
    //    var rawParameters = form.ToDictionary(f => f.Key, f => f.Value.ToString());

    //    var result = await _orchestrationService.HandleGatewayCallbackAsync(
    //        new GatewayCallbackData(transactionId, rawParameters), cancellationToken);

    //    var returnUrl = await _orchestrationService.GetReturnUrlAsync(transactionId, cancellationToken);
    //    return Redirect($"{returnUrl}?transactionId={transactionId}&paid={result.IsPaid}");
    //}

    /// <summary>تایید نهایی (Verify + Settle) بعد از اینکه سرویس مصرف‌کننده موجودی را بررسی کرد</summary>
    [HttpPost("{transactionId:long}/confirm")]
    [ServiceFilter(typeof(IdempotencyKeyFilter))]
    public async Task<ActionResult<ConfirmPaymentResult>> Confirm(z
        long transactionId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orchestrationService.ConfirmPaymentAsync(transactionId, cancellationToken);
            return result.IsSuccess ? Ok(result) : UnprocessableEntity(result);
        }
        catch (NotFoundException ex) { return NotFound(ex.Message); }
    }

    /// <summary>استعلام وضعیت با شناسه داخلی تراکنش</summary>
    [HttpGet("{transactionId:guid}/status")]
    public async Task<ActionResult<PaymentStatusResult>> GetStatus(
        long transactionId, 
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _orchestrationService
                .GetStatusAsync(transactionId, cancellationToken));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// استعلام وضعیت تراکنش با شماره سفارش
    /// </summary>
    [HttpGet("by-order/{orderId}/status")]
    public async Task<ActionResult<PaymentStatusResult>> GetStatusByOrder(
        long orderId, 
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _orchestrationService
                .GetStatusByOrderIdAsync(orderId, cancellationToken));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// برگشت وجه
    /// </summary>
    [HttpPost("{transactionId:guid}/reverse")]
    [ServiceFilter(typeof(IdempotencyKeyFilter))]
    public async Task<ActionResult<ConfirmPaymentResult>> Reverse(
        long transactionId, 
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orchestrationService
                .ReverseAsync(transactionId, cancellationToken);

            return result.IsSuccess ? Ok(result) : UnprocessableEntity(result);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
