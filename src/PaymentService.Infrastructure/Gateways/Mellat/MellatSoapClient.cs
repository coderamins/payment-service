using Microsoft.Extensions.Logging;
using PaymentService.Application.Common.Exceptions;
using System.Text;
using System.Xml.Linq;

namespace PaymentService.Infrastructure.Gateways.Mellat;

/// <summary>
/// سرویس بانک ملت
/// </summary>
public class MellatSoapClient
{
    private const string Namespace = "http://interfaces.core.sw.bps.com/";
    private readonly HttpClient _httpClient;
    private readonly MellatOptions _options;
    private readonly ILogger<MellatSoapClient> _logger;

    public MellatSoapClient(HttpClient httpClient, MellatOptions options, ILogger<MellatSoapClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public Task<string> BpPayRequestAsync(
        string orderId, long amount, string localDate, string localTime,
        string callbackUrl, string? additionalData, string? payerId, CancellationToken ct)
    {
        var parameters = new (string Name, string Value)[]
        {
            ("terminalId", _options.TerminalId),
            ("userName", _options.UserName),
            ("userPassword", _options.Password),
            ("orderId", orderId),
            ("amount", amount.ToString()),
            ("localDate", localDate),
            ("localTime", localTime),
            ("additionalData", additionalData ?? ""),
            ("callBackUrl", callbackUrl),
            ("payerId", payerId ?? ""),
        };

        return CallAsync("bpPayRequest", parameters, ct);
    }

    public Task<string> BpVerifyRequestAsync(string orderId, string saleOrderId, string saleReferenceId, CancellationToken ct)
        => CallAsync("bpVerifyRequest", CommonInquiryParams(orderId, saleOrderId, saleReferenceId), ct);

    public Task<string> BpSettleRequestAsync(string orderId, string saleOrderId, string saleReferenceId, CancellationToken ct)
        => CallAsync("bpSettleRequest", CommonInquiryParams(orderId, saleOrderId, saleReferenceId), ct);

    public Task<string> BpReversalRequestAsync(string orderId, string saleOrderId, string saleReferenceId, CancellationToken ct)
        => CallAsync("bpReversalRequest", CommonInquiryParams(orderId, saleOrderId, saleReferenceId), ct);

    private (string, string)[] CommonInquiryParams(string orderId, string saleOrderId, string saleReferenceId) => new (string, string)[]
    {
        ("terminalId", _options.TerminalId),
        ("userName", _options.UserName),
        ("userPassword", _options.Password),
        ("orderId", orderId),
        ("saleOrderId", saleOrderId),
        ("saleReferenceId", saleReferenceId),
    };

    private async Task<string> CallAsync(string operationName, (string Name, string Value)[] parameters, CancellationToken ct)
    {
        var envelope = BuildEnvelope(operationName, parameters);

        using var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", $"\"\"");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(_options.WebServiceUrl, content, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "خطا در ارتباط با وب‌سرویس بانک ملت هنگام {Operation}", operationName);
            throw new GatewayCommunicationException($"عدم دسترسی به وب‌سرویس بانک ملت ({operationName})"/*, ex*/);
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var faultReason = TryExtractSoapFaultReason(responseBody);
            _logger.LogError("پاسخ غیرموفق از بانک ملت. StatusCode={StatusCode} Body={Body}", response.StatusCode, responseBody);

            var message = faultReason is not null
                ? $"بانک ملت درخواست را رد کرد: {faultReason}"
                : $"پاسخ غیرموفق از بانک ملت با کد {(int)response.StatusCode}";

            throw new GatewayCommunicationException(message);
        }

        return ExtractReturnValue(responseBody, operationName);
    }

    private static string BuildEnvelope(string operationName, (string Name, string Value)[] parameters)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:ns=\"" + Namespace + "\">");
        sb.Append("<soapenv:Header/>");
        sb.Append("<soapenv:Body>");
        sb.Append($"<ns:{operationName}>");
        foreach (var (name, value) in parameters)
        {
            sb.Append($"<{name}>{System.Security.SecurityElement.Escape(value)}</{name}>");
        }
        sb.Append($"</ns:{operationName}>");
        sb.Append("</soapenv:Body>");
        sb.Append("</soapenv:Envelope>");
        return sb.ToString();
    }

    private static string ExtractReturnValue(string soapXml, string operationName)
    {
        try
        {
            var doc = XDocument.Parse(soapXml);
            var returnElement = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals($"{operationName}Return", StringComparison.OrdinalIgnoreCase));

            if (returnElement is null)
                throw new GatewayCommunicationException($"فیلد {operationName}Return در پاسخ بانک یافت نشد");

            return returnElement.Value;
        }
        catch (System.Xml.XmlException ex)
        {
            throw new GatewayCommunicationException($"پاسخ دریافتی از بانک ملت XML معتبر نیست ({operationName})"/*, ex*/);
        }
    }

    /// <summary>تلاش برای استخراج پیام علت خطا از یک پاسخ SOAP Fault استاندارد</summary>
    private static string? TryExtractSoapFaultReason(string soapXml)
    {
        try
        {
            var doc = XDocument.Parse(soapXml);

            var faultString = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value;
            if (faultString is not null) return faultString;

            var reasonText = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Text")?.Value;
            return reasonText;
        }
        catch
        {
            return null; 
        }
    }
}
