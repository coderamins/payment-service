using Microsoft.Extensions.Logging.Abstractions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Gateways.Pasargad;
using System.Net;
using System.Text;

namespace paymentService.UnitTests;

public class PasargadGatewayTests
{
    private const string TokenJson = "{\"resultMsg\":\"Successful\",\"resultCode\":0,\"token\":\"abc\"}";

    private static PaymentTransaction NewTransaction(long amount = 1000)
    {
        var tx = PaymentTransaction.Create(
            1, EPaymentSubscriber.Registration, amount, "https://example.com/return", GatewayProvider.Pasargad);
        tx.MarkRedirectedToGateway("urlId-1");
        return tx;
    }

    private static PasargadGateway CreateGateway(StubHandler handler)
    {
        var options = new PasargadOptions
        {
            Username = "u",
            Password = "p",
            TerminalNumber = 1,
            ApiBaseUrl = "https://pasargad.test"
        };

        var client = new PasargadApiClient(
            new HttpClient(handler), options, new PasargadTokenCache(),
            NullLogger<PasargadApiClient>.Instance);

        return new PasargadGateway(client, NullLogger<PasargadGateway>.Instance);
    }

    // ------------------------------------------------------------ ParseCallback

    [Fact]
    public void ParseCallback_Success_ReturnsReferenceNumber()
    {
        var gateway = CreateGateway(new StubHandler(_ => Json(TokenJson)));

        var result = gateway.ParseCallback(new Dictionary<string, string>
        {
            ["invoiceId"] = "10001",
            ["status"] = "success",
            ["referenceNumber"] = "142536124562",
            ["trackId"] = "123456"
        });

        Assert.True(result.IsSuccessfulPayment);
        Assert.Equal("142536124562", result.SaleReferenceId);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expire")]
    [InlineData("failed")]
    [InlineData("unknown")]
    public void ParseCallback_NonSuccessStatuses_AreNotSuccessful(string status)
    {
        var gateway = CreateGateway(new StubHandler(_ => Json(TokenJson)));

        var result = gateway.ParseCallback(new Dictionary<string, string> { ["status"] = status });

        Assert.False(result.IsSuccessfulPayment);
        Assert.Null(result.SaleReferenceId);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void ParseCallback_MissingStatus_IsNotSuccessful()
    {
        var gateway = CreateGateway(new StubHandler(_ => Json(TokenJson)));

        Assert.False(gateway.ParseCallback(new Dictionary<string, string>()).IsSuccessfulPayment);
    }

    // ------------------------------------------------------------ توکن و درخواست‌ها

    [Fact]
    public async Task RequestPayment_ReusesCachedToken_AndReturnsUrlAndUrlId()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/token/getToken")
            ? Json(TokenJson)
            : Json("{\"resultMsg\":\"Successful\",\"resultCode\":0,\"data\":{\"urlId\":\"U1\",\"url\":\"https://pay.test/U1\"}}"));
        var gateway = CreateGateway(handler);

        var first = await gateway.RequestPaymentAsync(NewTransaction(), "https://cb.test/x");
        var second = await gateway.RequestPaymentAsync(NewTransaction(), "https://cb.test/x");

        Assert.True(first.IsSuccess);
        Assert.Equal("U1", first.RefId);
        Assert.Equal("https://pay.test/U1", first.RedirectUrl);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, handler.Paths.Count(p => p.EndsWith("/token/getToken")));
    }

    [Fact]
    public async Task RequestPayment_On401_RefreshesTokenOnceAndRetries()
    {
        var purchaseCalls = 0;
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/token/getToken")) return Json(TokenJson);

            return ++purchaseCalls == 1
                ? Json("{}", HttpStatusCode.Unauthorized)
                : Json("{\"resultMsg\":\"Successful\",\"resultCode\":0,\"data\":{\"urlId\":\"U2\",\"url\":\"https://pay.test/U2\"}}");
        });
        var gateway = CreateGateway(handler);

        var result = await gateway.RequestPaymentAsync(NewTransaction(), "https://cb.test/x");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Paths.Count(p => p.EndsWith("/token/getToken")));
        Assert.Equal(2, purchaseCalls);
    }

    // ------------------------------------------------------------ Verify / Settle / Reverse

    [Fact]
    public async Task Verify_AmountMismatch_FailsAndReverses()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/token/getToken")) return Json(TokenJson);
            if (path.EndsWith("/verify-payment"))
                return Json("{\"resultMsg\":\"Successful\",\"resultCode\":0,\"data\":{\"invoice\":\"0\",\"referenceNumber\":\"1\",\"trackId\":\"2\",\"maskedCardNumber\":\"603770******0340\",\"amount\":999}}");
            return Json("{\"resultMsg\":\"Successful\",\"resultCode\":0}");
        });
        var gateway = CreateGateway(handler);

        var result = await gateway.VerifyAsync(NewTransaction(amount: 1000));

        Assert.False(result.IsSuccess);
        Assert.Contains(handler.Paths, p => p.EndsWith("/reverse-transactions"));
    }

    [Fact]
    public async Task Verify_Success_ReturnsMaskedCard()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/token/getToken")
            ? Json(TokenJson)
            : Json("{\"resultMsg\":\"Successful\",\"resultCode\":0,\"data\":{\"invoice\":\"0\",\"referenceNumber\":\"1\",\"trackId\":\"2\",\"maskedCardNumber\":\"603770******0340\",\"amount\":1000}}"));
        var gateway = CreateGateway(handler);

        var result = await gateway.VerifyAsync(NewTransaction(amount: 1000));

        Assert.True(result.IsSuccess);
        Assert.Equal("603770******0340", result.MaskedCardNumber);
    }

    [Theory]
    [InlineData(13029)] // قبلاً confirm شده؛ تکرار امن
    public async Task Settle_AlreadyConfirmed_IsTreatedAsSuccess(int code)
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/token/getToken")
            ? Json(TokenJson)
            : Json($"{{\"resultMsg\":\"Transaction status is confirmed\",\"resultCode\":{code}}}"));
        var gateway = CreateGateway(handler);

        Assert.True((await gateway.SettleAsync(NewTransaction())).IsSuccess);
    }

    [Fact]
    public async Task Reverse_Rejected_ReturnsFarsiMessage()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/token/getToken")
            ? Json(TokenJson)
            : Json("{\"resultMsg\":\"Reverse time expire\",\"resultCode\":13076}"));
        var gateway = CreateGateway(handler);

        var result = await gateway.ReverseAsync(NewTransaction());

        Assert.False(result.IsSuccess);
        Assert.Equal("13076", result.ResultCode);
        Assert.Equal("زمان برگشت تراکنش منقضی شده است", result.ErrorMessage);
    }

    // ------------------------------------------------------------ ابزار تست

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public List<string> Paths { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(_respond(request));
        }
    }
}
