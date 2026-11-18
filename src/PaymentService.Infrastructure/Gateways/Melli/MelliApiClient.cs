
using global::PaymentService.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Gateways.Melli;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentService.Infrastructure.Gateways.Sedad;

public class MelliApiClient
{
    private readonly HttpClient _http;
    private readonly MelliOptions _options;
    private readonly ILogger<MelliApiClient> _logger;

    public MelliApiClient(HttpClient http, MelliOptions options,
        ILogger<MelliApiClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _http.BaseAddress = new Uri(options.ApiBaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }

    public Task<MelliPaymentResponse?> PaymentRequestAsync(long orderId, decimal amount,
        string returnUrl, IReadOnlyList<PaymentSplitShare>? splits, CancellationToken ct)
    {
        var sign = MelliSigner
            .Sign($"{_options.TerminalId};{orderId};{amount}", _options.TerminalKey);

        var body = new Dictionary<string, object?>
        {
            ["MerchantId"] = _options.MerchantId,
            ["TerminalId"] = _options.TerminalId,
            ["Amount"] = amount,
            ["OrderId"] = orderId,
            ["ReturnUrl"] = returnUrl,
            ["SignData"] = sign,
            ["LocalDateTime"] = IranNow()
        };

        if (splits is { Count: > 0 })
        {
            body["MultiplexingData"] = new
            {
                Type = "Amount",
                MultiplexingRows = splits
                    .Select(s => new { IbanNumber = "IR" + s.Sheba, Value = s.AmountInRials })
                    .ToArray()
            };
        }

        return PostAsync<MelliPaymentResponse>("/api/v0/Request/PaymentRequest", body, ct);
    }


    public static DateTime IranNow()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
    }

    public Task<MelliVerifyResponse?> VerifyAsync(string token, CancellationToken ct)
        => PostAsync<MelliVerifyResponse>("/api/v0/Advice/verify",
            new
            {
                Token = token,
                SignData = MelliSigner.Sign(token, _options.TerminalKey)
            }, ct);

    public Task<MelliReverseResponse?> ReverseAsync(string token, decimal amount, CancellationToken ct)
        => PostAsync<MelliReverseResponse>("/api/v0/ReverseRequest",
            new
            {
                Token = token,
                SignData = MelliSigner.Sign(token, _options.TerminalKey),
                TerminalId = _options.TerminalId,
                CardAcqId = _options.MerchantId,
                Amount = amount
            },
            ct);

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
    {
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
            using var content = JsonContent.Create(body, options: jsonOptions);
            var response = await _http.PostAsync(path, content, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("پاسخ غیرموفق از سداد. Path={Path} Status={Status} Body={Body}",
                    path, (int)response.StatusCode, text);
                return null;
            }
            return JsonSerializer.Deserialize<T>(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "خطا در ارتباط با سداد: {Path}", path);
            throw new GatewayCommunicationException($"عدم دسترسی به وب‌سرویس سداد ({path})");
        }
    }
}

public class MelliPaymentResponse
{
    [JsonPropertyName("ResCode")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public short ResCode { get; set; }
    [JsonPropertyName("Token")] public string? Token { get; set; }
    [JsonPropertyName("Description")] public string? Description { get; set; }
}

public class MelliVerifyResponse
{
    [JsonPropertyName("ResCode")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public short ResCode { get; set; }

    [JsonPropertyName("Amount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Amount { get; set; }

    [JsonPropertyName("Description")] public string? Description { get; set; }
    [JsonPropertyName("RetrivalRefNo")] public string? RetrivalRefNo { get; set; }
    [JsonPropertyName("SystemTraceNo")] public string? SystemTraceNo { get; set; }

    [JsonPropertyName("OrderId")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public string? OrderId { get; set; }
}

public class MelliReverseResponse
{

    [JsonPropertyName("ResCode")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public short ResCode { get; set; }

    [JsonPropertyName("Amount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Amount { get; set; }

    [JsonPropertyName("Description")] public string? Description { get; set; }
    [JsonPropertyName("RetrivalRefNo")] public string? RetrivalRefNo { get; set; }
    [JsonPropertyName("SystemTraceNo")] public string? SystemTraceNo { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [JsonPropertyName("OrderId")]
    public long OrderId { get; set; }

    [JsonPropertyName("TransactionDate")] public string? TransactionDate { get; set; }
    [JsonPropertyName("CardHolderFullName")] public string? CardHolderFullName { get; set; }
}




