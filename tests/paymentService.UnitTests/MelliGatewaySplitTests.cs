using Microsoft.Extensions.Logging.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Gateways.Sedad;
using System.Net;
using System.Text;
using System.Text.Json;

namespace paymentService.UnitTests;

public class MelliGatewaySplitTests
{
    // دو شبای معتبر (checksum درست) بدون IR، مثل چیزی که در تراکنش ذخیره می‌شود
    private const string ShebaA = "430570023980001305443101";
    private const string ShebaB = "790570022580001305443001";

    private static (MelliGateway Gateway, CapturingHandler Handler) Create(string? defaultSheba = null)
    {
        var options = new MelliOptions
        {
            MerchantId = "m1",
            TerminalId = "t1",
            // کلید TripleDES (۲۴ بایت، غیر ضعیف) به‌صورت Base64
            TerminalKey = Convert.ToBase64String(Enumerable.Range(1, 24).Select(i => (byte)i).ToArray()),
            DefaultSheba = defaultSheba
        };

        var handler = new CapturingHandler();
        var api = new MelliApiClient(new HttpClient(handler), options, NullLogger<MelliApiClient>.Instance);
        var gateway = new MelliGateway(api, options, NullLogger<MelliGateway>.Instance);

        return (gateway, handler);
    }

    private static PaymentTransaction NewTransaction(params PaymentSplitShare[] splits)
        => PaymentTransaction.Create(
            1, EPaymentSubscriber.Registration, 1000, "https://example.com/return", GatewayProvider.Sadad,
            splits: splits.Length == 0 ? null : splits);

    [Fact]
    public async Task RequestPayment_WithSplits_SendsMultiplexingDataWithIrPrefixAndAmountType()
    {
        var (gateway, handler) = Create();

        var result = await gateway.RequestPaymentAsync(
            NewTransaction(new PaymentSplitShare(ShebaA, 600), new PaymentSplitShare(ShebaB, 400)),
            "https://cb.test/x");

        Assert.True(result.IsSuccess);
        Assert.Equal("tok", result.RefId);
        Assert.EndsWith("/tok", result.RedirectUrl);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var multiplexing = doc.RootElement.GetProperty("MultiplexingData");
        Assert.Equal("Amount", multiplexing.GetProperty("Type").GetString());

        var rows = multiplexing.GetProperty("MultiplexingRows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("IR" + ShebaA, rows[0].GetProperty("IbanNumber").GetString());
        Assert.Equal(600, rows[0].GetProperty("Value").GetInt64());
        Assert.Equal("IR" + ShebaB, rows[1].GetProperty("IbanNumber").GetString());
        Assert.Equal(400, rows[1].GetProperty("Value").GetInt64());
    }

    [Fact]
    public async Task RequestPayment_WithoutSplits_DoesNotSendMultiplexingData()
    {
        var (gateway, handler) = Create();

        await gateway.RequestPaymentAsync(NewTransaction(), "https://cb.test/x");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.False(doc.RootElement.TryGetProperty("MultiplexingData", out _));
        Assert.Equal("m1", doc.RootElement.GetProperty("MerchantId").GetString());
        Assert.Equal("t1", doc.RootElement.GetProperty("TerminalId").GetString());
    }

    [Fact]
    public async Task RequestPayment_DefaultSheba_SendsWholeAmountToOneAccount()
    {
        // ترمینال «تسهیم از سمت پذیرنده»: پرداخت بدون تسهیم هم باید MultiplexingData داشته باشد
        var (gateway, handler) = Create(defaultSheba: "IR" + ShebaA);

        await gateway.RequestPaymentAsync(NewTransaction(), "https://cb.test/x");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var multiplexing = doc.RootElement.GetProperty("MultiplexingData");
        Assert.Equal("Amount", multiplexing.GetProperty("Type").GetString());

        var rows = multiplexing.GetProperty("MultiplexingRows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("IR" + ShebaA, rows[0].GetProperty("IbanNumber").GetString());
        Assert.Equal(1000, rows[0].GetProperty("Value").GetInt64());
    }

    [Fact]
    public async Task RequestPayment_ExplicitSplits_TakePrecedenceOverDefaultSheba()
    {
        var (gateway, handler) = Create(defaultSheba: ShebaA);

        await gateway.RequestPaymentAsync(
            NewTransaction(new PaymentSplitShare(ShebaA, 300), new PaymentSplitShare(ShebaB, 700)),
            "https://cb.test/x");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var rows = doc.RootElement.GetProperty("MultiplexingData")
            .GetProperty("MultiplexingRows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task RequestPayment_InvalidDefaultSheba_Throws()
    {
        var (gateway, _) = Create(defaultSheba: "IR000000000000000000000000");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.RequestPaymentAsync(NewTransaction(), "https://cb.test/x"));
    }

    [Fact]
    public async Task RequestPayment_SignData_IsNotAffectedBySplits()
    {
        // طبق مستند، امضا فقط TerminalId;OrderId;Amount است
        var (plainGateway, plainHandler) = Create();
        var (splitGateway, splitHandler) = Create();

        await plainGateway.RequestPaymentAsync(NewTransaction(), "https://cb.test/x");
        await splitGateway.RequestPaymentAsync(
            NewTransaction(new PaymentSplitShare(ShebaA, 600), new PaymentSplitShare(ShebaB, 400)),
            "https://cb.test/x");

        Assert.Equal(SignDataOf(plainHandler), SignDataOf(splitHandler));
    }

    private static string? SignDataOf(CapturingHandler handler)
    {
        using var doc = JsonDocument.Parse(handler.LastBody!);
        return doc.RootElement.GetProperty("SignData").GetString();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"ResCode\":0,\"Token\":\"tok\",\"Description\":\"ok\"}", Encoding.UTF8, "application/json")
            };
        }
    }
}