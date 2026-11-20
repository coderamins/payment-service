using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using System.Text.Json;

namespace PaymentService.Infrastructure.Gateways.IranKish;

public class IranKishGateway : IPaymentGateway
{
    private readonly IranKishApiClient _apiClient;
    private readonly IranKishOptions _options;
    private readonly ILogger<IranKishGateway> _logger;

    public GatewayProvider Provider => GatewayProvider.IranKish;

    public IranKishGateway(IranKishApiClient apiClient, IranKishOptions options, ILogger<IranKishGateway> logger)
    {
        _apiClient = apiClient;
        _options = options;
        _logger = logger;
    }

    public async Task<GatewayRequestResult> RequestPaymentAsync(PaymentTransaction transaction, string callbackUrl,
        string? payerNationalCode = null, CancellationToken cancellationToken = default)
    {
        var result = await _apiClient.RequestTokenAsync(transaction.PaymentTransactionId.ToString(), transaction.AmountInRials, callbackUrl, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Token))
        {
            _logger.LogWarning("دریافت توکن ایران‌کیش برای سفارش {OrderId} ناموفق بود", transaction.OrderId);
            return new GatewayRequestResult(false, null, null, result.Error);
        }
        return new GatewayRequestResult(true, result.Token, _options.BaseUrl.TrimEnd('/') + "/iuiv3/IPG/Index/", null,
            RedirectMethod: "POST", RedirectFormFields: new Dictionary<string, string> { ["tokenIdentity"] = result.Token });
    }

    public GatewayCallbackParseResult ParseCallback(IDictionary<string, string> rawParameters)
    {
        var token = GetParameter(rawParameters, "token") ?? GetParameter(rawParameters, "tokenIdentity");
        var rrn = GetParameter(rawParameters, "retrievalReferenceNumber");
        var stan = GetParameter(rawParameters, "systemTraceAuditNumber");
        var success = !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(rrn) && !string.IsNullOrWhiteSpace(stan);

        // The shared callback contract has one reference field; preserve the three confirmation inputs in it.
        var reference = success ? JsonSerializer.Serialize(new CallbackReference(token!, rrn!, stan!)) : null;
        return new GatewayCallbackParseResult(
            success, 
            success ? "0" : "unknown", 
            reference, 
            null,
            success ? null : "اطلاعات بازگشتی ایران‌کیش ناقص است",
            null,
            token ?? string.Empty
            );
    }

    // Purchase confirmation is the final operation and is performed in SettleAsync.
    public Task<GatewayOperationResult> VerifyAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayOperationResult(true, "0", null));

    public async Task<GatewayOperationResult> SettleAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        CallbackReference? reference;
        try { reference = JsonSerializer.Deserialize<CallbackReference>(transaction.GatewaySaleReferenceId ?? ""); }
        catch (JsonException) { reference = null; }
        if (reference is null || string.IsNullOrWhiteSpace(reference.Token) || string.IsNullOrWhiteSpace(reference.Rrn) ||
            string.IsNullOrWhiteSpace(reference.Stan) || string.IsNullOrWhiteSpace(transaction.GatewayRefId) ||
            !string.Equals(reference.Token, transaction.GatewayRefId, StringComparison.Ordinal))
            return new GatewayOperationResult(false, null, "اطلاعات تأیید ایران‌کیش نامعتبر یا ناقص است");

        var success = await _apiClient.ConfirmAsync(reference.Token, reference.Rrn, reference.Stan,
            transaction.PaymentTransactionId.ToString(), transaction.AmountInRials, cancellationToken);
        return new GatewayOperationResult(success, success ? "0" : null, success ? null : "تأیید خرید ایران‌کیش ناموفق بود");
    }

    public Task<GatewayOperationResult> ReverseAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayOperationResult(false, null, "عملیات برگشت وجه ایران‌کیش در نمونه ارائه‌شده پشتیبانی نشده است"));

    private static string? GetParameter(IDictionary<string, string> parameters, string name)
    {
        foreach (var pair in parameters)
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    private record CallbackReference(string Token, string Rrn, string Stan);
}
