
using global::PaymentService.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentService.Infrastructure.Gateways.PardakhtNovin;

public class PardakhtNovinApiClient
{
    private readonly HttpClient _http;
    private readonly PardakhtNovinOptions _options;
    private readonly ILogger<PardakhtNovinApiClient> _logger;

    public PardakhtNovinApiClient(HttpClient http, PardakhtNovinOptions options,
        ILogger<PardakhtNovinApiClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _http.BaseAddress = new Uri(options.ApiBaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }

    public Task<NovinTokenResponse?> NormalSaleAsync(long orderId, decimal amount,
        string callbackUrl, CancellationToken ct)
        => PostAsync<NovinTokenResponse>("/mhipg/api/Payment/NormalSale", new
        {
            CorporationPin = _options.CorporationPin,
            Amount = amount,
            OrderId = orderId,
            CallBackUrl = callbackUrl
        }, ct);

    public Task<NovinConfirmResponse?> ConfirmAsync(string token, CancellationToken ct)
        => PostAsync<NovinConfirmResponse>("/mhipg/api/Payment/confirm",
            new { CorporaionPin = _options.CorporationPin, Token = token }, ct);

    public Task<NovinTokenResponse?> ReverseAsync(string token, CancellationToken ct)
        => PostAsync<NovinTokenResponse>("/mhipg/api/Payment/Reverse",
            new { CorporaionPin = _options.CorporationPin, Token = token }, ct);

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
    {
        try
        {
            // نام پراپرتی‌ها همان‌طور که هست ارسال شود (بدون camelCase)
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
            using var content = JsonContent.Create(body, options: jsonOptions);
            var response = await _http.PostAsync(path, content, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("پاسخ غیرموفق از پرداخت نوین. Path={Path} Status={Status} Body={Body}",
                    path, (int)response.StatusCode, text);
                return null;
            }
            return JsonSerializer.Deserialize<T>(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "خطا در ارتباط با پرداخت نوین: {Path}", path);
            throw new GatewayCommunicationException($"عدم دسترسی به وب‌سرویس پرداخت نوین ({path})");
        }
    }
}

public class NovinTokenResponse
{
    [JsonPropertyName("Token")] public string? Token { get; set; }
    [JsonPropertyName("Message")] public string? Message { get; set; }
    [JsonPropertyName("Status")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public short Status { get; set; }
}

public class NovinConfirmResponse
{
    [JsonPropertyName("Status")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public short Status { get; set; }
    [JsonPropertyName("CardNumberMasked")] public string? CardNumberMasked { get; set; }
    [JsonPropertyName("RRN")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Rrn { get; set; }
    [JsonPropertyName("Token")] public string? Token { get; set; }
}

/// <summary>RRN در مستند Long است ولی در نمونه رشته آمده؛ هر دو را می‌خواند</summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
            JsonTokenType.Null => null,
            _ => throw new JsonException()
        };

    public override void Write(Utf8JsonWriter w, string? v, JsonSerializerOptions o) => w.WriteStringValue(v);
}
