

using Microsoft.Extensions.Logging;
using PaymentService.Application.Common.Exceptions;
using PaymentService.Infrastructure.Gateways.Parsian;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PaymentService.Infrastructure.Gateways.Saman;

public class SamanApiClient
{
    private readonly HttpClient _httpClient;
    private readonly SamanOptions _options;
    private readonly ILogger<SamanApiClient> _logger;

    public SamanApiClient(HttpClient httpClient, SamanOptions options,  ILogger<SamanApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public Task<SamanTokenResponse> RequestTokenAsync(
       long transactionId, decimal amount, string callbackUrl, string? cellNumber, CancellationToken ct)
        => PostAsync<SamanTokenResponse>("onlinepg/OnlinePG", new
        {
            Action = "Token",
            TerminalId = _options.TerminalId.ToString(),
            RedirectUrl = callbackUrl,
            ResNum = transactionId.ToString(),
            Amount = amount,
            CellNumber = cellNumber
        }, ct);

    public Task<SamanConfirmResponse> ConfirmAsync(
        string refNum, long terminalNumber, CancellationToken ct)
        => PostAsync<SamanConfirmResponse>("verifyTxnRandomSessionkey/ipg/VerifyTranscation", new
        {
            RefNum = refNum,
            TerminalNumber = terminalNumber
        }, ct);

    public Task<SamanConfirmResponse> ReversalAsync(
        string refNum, long terminalNumber, CancellationToken ct)
        => PostAsync<SamanConfirmResponse>("verifyTxnRandomSessionkey/ipg/ReverseTranscation", new
        {
            RefNum = refNum,
            TerminalNumber = terminalNumber
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
                _logger.LogWarning("پاسخ غیرموفق از سامان. Path={Path} Http={Status} Body={Body}",
                    path, (int)response.StatusCode, errorBody);
                throw new GatewayCommunicationException($"پاسخ نامعتبر از سامان (HTTP {(int)response.StatusCode})");
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                   ?? throw new GatewayCommunicationException("پاسخ خالی از سامان دریافت شد");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "خطا در ارتباط با سامان: {Path}", path);
            throw new GatewayCommunicationException($"عدم دسترسی به وب‌سرویس سامان ({path})");
        }
    }
}

public class SamanTokenResponse
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
    [JsonPropertyName("errorCode")] public string ErrorCode { get; set; } = string.Empty;
    [JsonPropertyName("errorDesc")] public string ErrorDesc { get; set; } = string.Empty;
}

public class SamanConfirmResponse
{
    [JsonPropertyName("TransactionDetail")] public VerifyInfo TransactionDetail { get; set; }
    [JsonPropertyName("ResultCode")] public int ResultCode { get; set; }
    [JsonPropertyName("ResultDescription")] public string ResultDescription { get; set; } = string.Empty;
    [JsonPropertyName("Success")] public bool Success { get; set; }
}

public class VerifyInfo
{
    [JsonPropertyName("RRN")] public string RRN { get; set; } = string.Empty;
    [JsonPropertyName("RefNum")] public string RefNum { get; set; } = string.Empty;
    [JsonPropertyName("MaskedPan")] public string MaskedPan { get; set; } = string.Empty;
    [JsonPropertyName("HashedPan")] public string HashedPan { get; set; } = string.Empty;
    [JsonPropertyName("TerminalNumber")] public int TerminalNumber { get; set; }
    [JsonPropertyName("OrginalAmount")] public long OrginalAmount { get; set; }
    [JsonPropertyName("AffectiveAmount")] public long AffectiveAmount { get; set; }
    [JsonPropertyName("StraceDate")] public string StraceDate { get; set; } = string.Empty;
    [JsonPropertyName("StraceNo")] public string StraceNo { get; set; } = string.Empty;
}