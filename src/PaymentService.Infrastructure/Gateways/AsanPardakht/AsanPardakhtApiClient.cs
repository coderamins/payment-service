

using Microsoft.Extensions.Logging;
using PaymentService.Application.Common.Exceptions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PaymentService.Infrastructure.Gateways.AsanPardakht;

public class AsanPardakhtApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AsanPardakhtOptions _options;
    private readonly ILogger<AsanPardakhtApiClient> _logger;

    public AsanPardakhtApiClient(
        HttpClient httpClient, 
        AsanPardakhtOptions options, 
        ILogger<AsanPardakhtApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.ApiBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Add("usr",_options.UserName);
        _httpClient.DefaultRequestHeaders.Add("pwd", _options.Password);
    }

    public async Task<(bool IsSuccess, string? Token, int StatusCode)> RequestTokenAsync(
       long localInvoiceId, long amountInRials, string callbackUrl, string? additionalData, CancellationToken ct)
    {
        var body = new
        {
            serviceTypeId = 1,
            merchantConfigurationId = _options.MerchantConfigurationId,
            localInvoiceId = localInvoiceId,
            amountInRials = amountInRials,
            localDate = GetIranLocalDate(),
            additionalData = additionalData ?? "",
            callbackURL = callbackUrl
        };

        var response = await SendAsync(HttpMethod.Post, "/v1/Token", body, ct);

        if (response.StatusCode != HttpStatusCode.OK)
            return (false, null, (int)response.StatusCode);

        var token = (await response.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
        return (true, token, 200);
    }

    public async Task<AsanPardakhtTranResult?> GetTransactionResultAsync(
        long localInvoiceId, 
        CancellationToken ct)
    {
        var url = $"/v1/TranResult?merchantConfigurationId={_options.MerchantConfigurationId}&localInvoiceId={localInvoiceId}";
        var response = await SendAsync(HttpMethod.Get, url, null, ct);

        // 472 یعنی رکوردی یافت نشد (تراکنش موفقی وجود ندارد) — خطا نیست، نبودِ نتیجه است
        if (response.StatusCode != HttpStatusCode.OK)
            return null;

        return await response.Content.ReadFromJsonAsync<AsanPardakhtTranResult>(cancellationToken: ct);
    }

    public Task<HttpResponseMessage> VerifyAsync(long payGateTranId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/v1/Verify", BuildTranBody(payGateTranId), ct);

    public Task<HttpResponseMessage> SettlementAsync(long payGateTranId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/v1/Settlement", BuildTranBody(payGateTranId), ct);

    /// <summary>برگشت وجه تراکنشِ هنوز Verify نشده</summary>
    public Task<HttpResponseMessage> ReverseAsync(long payGateTranId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/v1/Reverse", BuildTranBody(payGateTranId), ct);

    /// <summary>لغو تراکنشِ Verify شده (تا ۳ ساعت بعد از تراکنش)</summary>
    public Task<HttpResponseMessage> CancelAsync(long payGateTranId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/v1/Cancel", BuildTranBody(payGateTranId), ct);

    private object BuildTranBody(long payGateTranId) => new
    {
        merchantConfigurationId = _options.MerchantConfigurationId,
        payGateTranId = payGateTranId
    };

    private static string GetIranLocalDate()
    {
        var iranTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, iranTz);
        return now.ToString("yyyyMMdd HHmmss");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        try
        {
            var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("پاسخ غیر ۲۰۰ از آسان پرداخت. Url={Url} Status={Status} Body={Body}",
                    url, (int)response.StatusCode, errorBody);
            }

            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "خطا در ارتباط با آسان پرداخت: {Url}", url);
            throw new GatewayCommunicationException($"عدم دسترسی به وب‌سرویس آسان پرداخت ({url})"/*, ex*/);
        }
    }
}

public class AsanPardakhtTranResult
{
    [JsonPropertyName("payGateTranID")] public long PayGateTranId { get; set; }
    [JsonPropertyName("cardNumber")] public string? CardNumber { get; set; }
    [JsonPropertyName("rrn")] public string? Rrn { get; set; }
    [JsonPropertyName("refId")] public string? RefId { get; set; }
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("salesOrderID")] public string? SalesOrderId { get; set; }
    [JsonPropertyName("serviceStatusCode")] public string? ServiceStatusCode { get; set; }
}