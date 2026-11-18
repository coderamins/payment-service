
namespace PaymentService.Infrastructure.Gateways.Pasargad;

using global::PaymentService.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaymentService.Application.Common.Exceptions;


public class PasargadApiClient
{
    private const string PurchaseServiceCode = "8";
    private const string PurchaseServiceType = "PURCHASE";
    private const string MultiAccServiceCode = "9";
    private const string MultiAccServiceType = "MULTIACCPURCHASE";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PasargadOptions _options;
    private readonly PasargadTokenCache _tokenCache;
    private readonly ILogger<PasargadApiClient> _logger;

    public PasargadApiClient(
        HttpClient httpClient,
        PasargadOptions options,
        PasargadTokenCache tokenCache,
        ILogger<PasargadApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _tokenCache = tokenCache;
        _logger = logger;

        // عمداً Uri اینجا ساخته نمی‌شود تا تنظیم ناقص پاسارگاد پرداخت بقیه‌ی درگاه‌ها را خراب نکند
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public Task<PasargadEnvelope> PurchaseAsync(
        string invoice, DateTime invoiceDateUtc, long amount, string callbackApi,
        string? description, string? nationalCode,
        int? terminalNumber, IReadOnlyList<PaymentSplitShare>? splits, CancellationToken ct)
    {
        var terminal = terminalNumber ?? _options.TerminalNumber;

        if (splits is not { Count: > 0 })
        {
            return PostAuthorizedAsync("api/payment/purchase", new
            {
                invoice,
                invoiceDate = ToJalaliDate(invoiceDateUtc),
                amount,
                callbackApi,
                serviceCode = PurchaseServiceCode,
                serviceType = PurchaseServiceType,
                terminalNumber = terminal,
                description,
                nationalCode
            }, ct);
        }

        // خرید با تسهیم: sharedValue مبلغ به ریال (رشته) و شبا بدون IR
        return PostAuthorizedAsync("api/payment/multiacc/purchase", new
        {
            invoice,
            invoiceDate = ToJalaliDate(invoiceDateUtc),
            amount,
            callbackApi,
            serviceCode = MultiAccServiceCode,
            serviceType = MultiAccServiceType,
            terminalNumber = terminal,
            description,
            nationalCode,
            sharedValue = splits.Select(s => s.AmountInRials.ToString(CultureInfo.InvariantCulture)).ToArray(),
            sheba = splits.Select(s => s.Sheba).ToArray()
        }, ct);
    }

    public Task<PasargadEnvelope> VerifyAsync(string invoice, string urlId, CancellationToken ct)
        => PostAuthorizedAsync("api/payment/verify-payment", new { invoice, urlId }, ct);

    public Task<PasargadEnvelope> ConfirmAsync(string invoice, string urlId, CancellationToken ct)
        => PostAuthorizedAsync("api/payment/confirm-transactions", new { invoice, urlId }, ct);

    public Task<PasargadEnvelope> ReverseAsync(string invoice, string urlId, CancellationToken ct)
        => PostAuthorizedAsync("api/payment/reverse-transactions", new { invoice, urlId }, ct);

    // ---------------------------------------------------------------- زیرساخت HTTP

    private async Task<PasargadEnvelope> PostAuthorizedAsync(string path, object body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(ct);

            var (httpStatus, envelope) = await SendAsync(path, body, token, ct);

            var unauthorized = httpStatus == HttpStatusCode.Unauthorized || envelope?.ResultCode == 401;
            if (unauthorized && attempt == 0)
            {
                _logger.LogInformation("توکن پاسارگاد رد شد؛ احراز هویت مجدد. Path={Path}", path);
                await _tokenCache.InvalidateAsync(token, ct);
                continue;
            }

            if (envelope is null)
                throw new GatewayCommunicationException($"پاسخ نامعتبر از پاسارگاد (HTTP {(int)httpStatus})");

            return envelope;
        }
    }

    private Task<string> GetTokenAsync(CancellationToken ct)
        => _tokenCache.GetOrCreateAsync(
            async innerCt =>
            {
                var (httpStatus, envelope) = await SendAsync(
                    "token/getToken",
                    new { username = _options.Username, password = _options.Password },
                    bearerToken: null, innerCt);

                if (envelope is null || envelope.ResultCode != 0 || string.IsNullOrWhiteSpace(envelope.Token))
                {
                    _logger.LogError(
                        "دریافت توکن پاسارگاد ناموفق بود. Http={Http} ResultCode={Code} Msg={Msg}",
                        (int)httpStatus, envelope?.ResultCode, envelope?.ResultMsg);
                    throw new GatewayCommunicationException("احراز هویت نزد پاسارگاد ناموفق بود");
                }

                var expiresAt = TryReadJwtExpiry(envelope.Token)
                                ?? DateTime.UtcNow.AddSeconds(_options.DefaultTokenLifetimeSeconds);

                return (envelope.Token, expiresAt);
            },
            TimeSpan.FromSeconds(_options.TokenRefreshMarginSeconds),
            ct);

    private async Task<(HttpStatusCode Status, PasargadEnvelope? Envelope)> SendAsync(
        string path, object body, string? bearerToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl)
            || !Uri.TryCreate(_options.ApiBaseUrl.TrimEnd('/') + "/" + path, UriKind.Absolute, out var uri))
        {
            throw new GatewayCommunicationException(
                "تنظیمات درگاه پاسارگاد کامل نیست (PaymentGateways:Pasargad:ApiBaseUrl)");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
            };

            if (bearerToken is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            if (!string.IsNullOrWhiteSpace(_options.Referer))
                request.Headers.TryAddWithoutValidation("Referer", _options.Referer);

            using var response = await _httpClient.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            PasargadEnvelope? envelope = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { envelope = JsonSerializer.Deserialize<PasargadEnvelope>(text, JsonOptions); }
                catch (JsonException) { }
            }

            if (envelope?.ResultCode is null && !response.IsSuccessStatusCode)
            {
                _logger.LogWarning("پاسخ غیرموفق از پاسارگاد. Path={Path} Http={Status} Body={Body}",
                    path, (int)response.StatusCode, Truncate(text, 500));
                envelope = null;
            }

            return (response.StatusCode, envelope);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "خطا در ارتباط با پاسارگاد: {Path}", path);
            throw new GatewayCommunicationException($"عدم دسترسی به وب‌سرویس پاسارگاد ({path})");
        }
    }

    // ---------------------------------------------------------------- ابزارها

    private static string ToJalaliDate(DateTime utc)
    {
        var iran = utc.AddHours(3.5); // آفست ثابت +03:30
        var pc = new PersianCalendar();
        return string.Create(CultureInfo.InvariantCulture,
            $"{pc.GetYear(iran):0000}/{pc.GetMonth(iran):00}/{pc.GetDayOfMonth(iran):00}");
    }

    private static DateTime? TryReadJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
        }
        return null;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}

/// <summary>پوشش مشترک پاسخ‌های پاسارگاد: resultCode / resultMsg / data (/ token در getToken)</summary>
public sealed class PasargadEnvelope
{
    public int? ResultCode { get; set; }
    public string? ResultMsg { get; set; }
    public string? Token { get; set; }

    /// <summary>data بسته به سرویس شیء، رشته یا خالی است؛ پس JsonElement نگه داشته می‌شود</summary>
    public JsonElement Data { get; set; }

    public bool IsSuccess => ResultCode == 0;
    public int Code => ResultCode ?? -1;

    private static readonly JsonSerializerOptions DataOptions = new(JsonSerializerDefaults.Web);

    public T? ReadData<T>() where T : class
    {
        if (Data.ValueKind != JsonValueKind.Object) return null;

        try { return Data.Deserialize<T>(DataOptions); }
        catch (JsonException) { return null; }
    }
}

public sealed class PasargadPurchaseData
{
    public string? UrlId { get; set; }
    public string? Url { get; set; }
}

public sealed class PasargadTransactionData
{
    public string? Invoice { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? TrackId { get; set; }
    public string? MaskedCardNumber { get; set; }
    public string? HashedCardNumber { get; set; }
    public string? RequestDate { get; set; }
    public long? Amount { get; set; }
}