using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;

namespace PaymentService.API.Filters;

public class IdempotencyKeyFilter : IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";
    private readonly IIdempotencyStore _store;

    public IdempotencyKeyFilter(IIdempotencyStore store)
    {
        _store = store;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var keyValues) || string.IsNullOrWhiteSpace(keyValues))
        {
            context.Result = new BadRequestObjectResult(new
            {
                error = $"هدر '{HeaderName}' برای این درخواست الزامی است تا از پردازش تکراری آن جلوگیری شود."
            });
            return;
        }

        var idempotencyKey = keyValues.ToString();
        var requestPath = httpContext.Request.Path.Value ?? "";

        var requestBody = await ReadRequestBodyAsync(httpContext);
        var requestHash = ComputeHash(requestBody);

        var existing = await _store.FindAsync(idempotencyKey, requestPath, httpContext.RequestAborted);

        if (existing is not null)
        {
            if (existing.RequestBodyHash != requestHash)
            {
                context.Result = new ObjectResult(new
                {
                    error = "این Idempotency-Key قبلاً با بدنه‌ی درخواست متفاوتی استفاده شده است."
                })
                { StatusCode = StatusCodes.Status409Conflict };
                return;
            }

            // درخواست کاملاً تکراری است - بدون اجرای دوباره‌ی Action، همان پاسخ قبلی برگردانده می‌شود
            var cachedValue = JsonSerializer.Deserialize<object>(existing.ResponseBodyJson);
            context.Result = new ObjectResult(cachedValue) { StatusCode = existing.ResponseStatusCode };
            httpContext.Response.Headers["Idempotent-Replayed"] = "true";
            return;
        }

        var executedContext = await next();

        if (executedContext.Exception is not null)
            return; // در صورت بروز خطای غیرمنتظره چیزی ذخیره نمی‌شود تا کاربر بتواند با همان کلید دوباره تلاش کند

        if (executedContext.Result is ObjectResult { Value: not null } objectResult)
        {
            var statusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;
            var responseJson = JsonSerializer.Serialize(objectResult.Value);

            var record = IdempotencyRecord.Create(idempotencyKey, requestPath, requestHash, statusCode, responseJson);
            await _store.SaveAsync(record, httpContext.RequestAborted);
        }
    }

    private static async Task<string> ReadRequestBodyAsync(HttpContext httpContext)
    {
        httpContext.Request.EnableBuffering();
        httpContext.Request.Body.Position = 0;

        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        httpContext.Request.Body.Position = 0; // برای اینکه بعداً Model Binder بتواند دوباره بدنه را بخواند
        return body;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
