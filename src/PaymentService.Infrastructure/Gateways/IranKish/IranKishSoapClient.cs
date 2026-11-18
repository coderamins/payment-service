using Microsoft.Extensions.Logging;
using PaymentService.Application.Common.Exceptions;
using System.Text;
using System.Text.Json;

namespace PaymentService.Infrastructure.Gateways.IranKish;

public class IranKishApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IranKishOptions _options;
    private readonly ILogger<IranKishApiClient> _logger;

    public IranKishApiClient(HttpClient httpClient, IranKishOptions options, ILogger<IranKishApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<(bool Success, string? Token, string? Error)> RequestTokenAsync(string requestId, decimal amount, string callbackUrl, CancellationToken ct)
    {
        var envelope = IranKishHelpers.CreateAuthenticationEnvelope(_options.TerminalId, _options.PassPhrase, amount, _options.RsaPubKeyBase64);
        var body = new
        {
            authenticationEnvelope = new { data = envelope.Data, iv = envelope.Iv },
            request = new
            {
                transactionType = "Purchase", terminalId = _options.TerminalId, acceptorId = _options.AcceptorId,
                amount, revertUri = callbackUrl, billInfo = (object?)null, multiplexParameters = (object?)null,
                paymentId = (object?)null, requestId, requestTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                additionalParameters = Array.Empty<object>()
            }
        };
        using var response = await PostAsync("/api/v3/tokenization/make", body, ct);
        var root = response.RootElement;
        var success = root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.True;
        string? token = null;
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("token", out var value) && value.ValueKind == JsonValueKind.String)
            token = value.GetString();
        return success && !string.IsNullOrWhiteSpace(token)
            ? (true, token, null) : (false, null, "دریافت توکن ایران‌کیش ناموفق بود");
    }

    public async Task<bool> ConfirmAsync(string token, string rrn, string stan, string invoiceId, decimal amount, CancellationToken ct)
    {
        var body = new { terminalId = _options.TerminalId, retrievalReferenceNumber = rrn, systemTraceAuditNumber = stan,
            tokenIdentity = token, invoiceId, amount };
        using var response = await PostAsync("/api/v3/confirmation/purchase", body, ct);
        return response.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.True;
    }

    private async Task<JsonDocument> PostAsync(string path, object body, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            using var response = await _httpClient.PostAsync(_options.BaseUrl.TrimEnd('/') + path, content, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("IranKish request failed. Path={Path} Status={Status}", path, response.StatusCode);
                throw new GatewayCommunicationException($"پاسخ ناموفق از ایران‌کیش با کد {(int)response.StatusCode}");
            }
            try { return JsonDocument.Parse(text); }
            catch (JsonException) { throw new GatewayCommunicationException("پاسخ ایران‌کیش JSON معتبر نیست"); }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "خطا در ارتباط با ایران‌کیش. Path={Path}", path);
            throw new GatewayCommunicationException("عدم دسترسی به وب‌سرویس ایران‌کیش");
        }
    }
}
