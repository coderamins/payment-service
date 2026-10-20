using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Common.Constants;
using PaymentService.Application.Common.Exceptions;
using PaymentService.Domain.Common;

namespace PaymentService.API.Exceptions;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
    HttpContext httpContext,
    Exception exception,
    CancellationToken cancellationToken)
    {
        _logger.LogError(exception,
            "یک خطای ناشناخته ایجاد شده است .");

        _logger.LogInformation("Exception Type: {Type}", exception.GetType().FullName);

        var problem = CreateProblemDetails(httpContext, exception);

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        problem.Extensions["correlationId"] =
            httpContext.Items[CorrelationConstants.ItemKey];

        httpContext.Response.StatusCode = problem.Status ?? 500;

        if (exception is GatewayUnavailableException { RetryAfter: { } retryAfter })
            httpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static ValidationProblemDetails CreateProblemDetails(
        HttpContext context,
        Exception exception)
    {
        if (exception is ValidationException validationException)
        {
            return new ValidationProblemDetails(validationException.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Failed",
                Detail = validationException.Message,
                Instance = context.Request.Path
            };
        }

        var (status, title) = exception switch
        {
            GatewayUnavailableException =>
                (StatusCodes.Status503ServiceUnavailable,
                    "Gateway Unavailable"),

            ValidationException =>
                (StatusCodes.Status400BadRequest,
                    "Validation Failed"),

            DomainException =>
                (StatusCodes.Status400BadRequest,
                    "Business Rule Violation"),

            NotFoundException =>
                (StatusCodes.Status404NotFound,
                    "Resource Not Found"),

            ConflictException =>
                (StatusCodes.Status409Conflict,
                    "Conflict"),

            UnauthorizedException =>
                (StatusCodes.Status401Unauthorized,
                    "Unauthorized"),

            ForbiddenException =>
                (StatusCodes.Status403Forbidden,
                    "Forbidden"),

            _ =>
                (StatusCodes.Status500InternalServerError,
                    "Internal Server Error")
        };

        var problem = new ValidationProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] =
            context.TraceIdentifier;

        return problem;
    }

}
