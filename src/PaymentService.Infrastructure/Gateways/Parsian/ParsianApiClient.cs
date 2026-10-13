namespace PaymentService.Infrastructure.Gateways.Parsian;

using global::PaymentService.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public class ParsianApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ParsianOptions _options;
    private readonly ILogger<ParsianApiClient> _logger;

    public ParsianApiClient(HttpClient httpClient, ParsianOptions options, ILogger<ParsianApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public Task<ParsianSaleResponse> SalePaymentAsync(
        long orderId, long amount, string callbackUrl, string? additionalData, CancellationToken ct)
        => PostAsync<ParsianSaleResponse>("Payment/SalePaymentRequest", new
        {
            LoginAccount = _options.LoginAccount,
            Amount = amount,
            OrderId = orderId,
            CallBackUrl = callbackUrl,
            AdditionalData = additionalData ?? ""
        }, ct);

    public Task<ParsianConfirmResponse> ConfirmAsync(
        long token, long orderId, long amount, CancellationToken ct)
        => PostAsync<ParsianConfirmResponse>("Payment/ConfirmPaymentWithAmount", new
        {
            LoginAccount = _options.LoginAccount,
            Token = token,
            OrderId = orderId,
            Amount = amount
        }, ct);

    public Task<ParsianReversalResponse> ReversalAsync(long token, CancellationToken ct)
        => PostAsync<ParsianReversalResponse>("Payment/ReversalRequest", new
        {
            LoginAccount = _options.LoginAccount,
            Token = token
        }, ct);

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct) where T : new()
    {
        try
        {
            // PropertyNamingPolicy=null تا نام فیلدها دقیقاً PascalCase (مطابق مستند) ارسال شود
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = null };
            using var response = await _httpClient.PostAsJsonAsync(path, body, options, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("پاسخ غیرموفق از پارسیان. Path={Path} Http={Status} Body={Body}",
                    path, (int)response.StatusCode, errorBody);
                throw new GatewayCommunicationException($"پاسخ نامعتبر از پارسیان (HTTP {(int)response.StatusCode})");
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                   ?? throw new GatewayCommunicationException("پاسخ خالی از پارسیان دریافت شد");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "خطا در ارتباط با پارسیان: {Path}", path);
            throw new GatewayCommunicationException($"عدم دسترسی به وب‌سرویس پارسیان ({path})");
        }
    }
}

public class ParsianSaleResponse
{
    [JsonPropertyName("Status")] public int Status { get; set; } = -1;
    [JsonPropertyName("Message")] public string? Message { get; set; }
    [JsonPropertyName("Token")] public long Token { get; set; }
    [JsonPropertyName("RedirectUrl")] public string? RedirectUrl { get; set; }
}

public class ParsianConfirmResponse
{
    [JsonPropertyName("Status")] public int Status { get; set; } = -1;
    [JsonPropertyName("CardNumberMasked")] public string? CardNumberMasked { get; set; }
    [JsonPropertyName("Token")] public long Token { get; set; }
    [JsonPropertyName("RRN")] public long Rrn { get; set; }
}

public class ParsianReversalResponse
{
    [JsonPropertyName("Status")] public int Status { get; set; } = -1;
    [JsonPropertyName("Message")] public string? Message { get; set; }
    [JsonPropertyName("Token")] public long Token { get; set; }
}