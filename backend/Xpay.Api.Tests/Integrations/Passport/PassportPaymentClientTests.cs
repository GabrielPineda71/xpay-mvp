using System.Net;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-373 — unit tests de PassportPaymentClient. SIN red real: mismo
// patrón exacto que PassportKeyClientTests (PassportHttpClient real sobre
// FakeHttpMessageHandler + FakePassportTokenProvider). Todos los valores
// son sintéticos: ningún account_id/resolution_id/payment_id real.
public class PassportPaymentClientTests
{
    private const string BaseUrl = "https://passport.test";

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [PassportOptions.EnvBaseUrl]      = BaseUrl,
        [PassportOptions.EnvClientId]     = "test-client-id",
        [PassportOptions.EnvClientSecret] = "test-client-secret",
    };

    private static PassportPaymentClient CreateClient(FakeHttpMessageHandler handler)
    {
        var http = new PassportHttpClient(
            new FakeHttpClientFactory(handler),
            new FakePassportTokenProvider("synthetic-bearer-token"),
            new FakeConfiguration(ValidConfig()),
            new CapturingLogger<PassportHttpClient>());
        return new PassportPaymentClient(http);
    }

    private static PassportCreatePaymentRequest SyntheticRequest() => new(
        AccountId: "synthetic-operational-account-id-001",
        ResolutionId: "synthetic-resolution-id-001",
        Amount: new PassportPaymentAmountRequest(Value: "5000.00", Currency: PassportPaymentClient.CopCurrency));

    private const string FullPaymentResponseBody = """
        {
          "id": "synthetic-payment-id-001",
          "status": "PROCESSING",
          "account_id": "synthetic-operational-account-id-001",
          "resolution_id": "synthetic-resolution-id-001",
          "amount": { "value": "5000.00", "currency": "COP" }
        }
        """;

    // ── CreateBrebPaymentAsync ────────────────────────────────────────────

    [Fact]
    public async Task CreateBrebPaymentAsync_PostsToExactContractPath()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);

        await client.CreateBrebPaymentAsync(SyntheticRequest());

        Assert.Equal($"{BaseUrl}/v1/payments/breb", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
    }

    [Fact]
    public async Task CreateBrebPaymentAsync_SerializesExactContractShape()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);

        await client.CreateBrebPaymentAsync(SyntheticRequest());

        using var sent = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var root = sent.RootElement;
        Assert.Equal("synthetic-operational-account-id-001", root.GetProperty("account_id").GetString());
        Assert.Equal("synthetic-resolution-id-001", root.GetProperty("resolution_id").GetString());
        Assert.Equal("5000.00", root.GetProperty("amount").GetProperty("value").GetString());
        Assert.Equal("COP", root.GetProperty("amount").GetProperty("currency").GetString());
        Assert.False(root.TryGetProperty("display_name", out _), "display_name no debe enviarse cuando es null.");
    }

    [Fact]
    public async Task CreateBrebPaymentAsync_Http200_DeserializesPaymentId()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);

        var result = await client.CreateBrebPaymentAsync(SyntheticRequest());

        Assert.Equal("synthetic-payment-id-001", result.Id);
        Assert.Equal("PROCESSING", result.Status);
    }

    // FASE 15 test #5 — payment response sin id → fallo seguro.
    [Fact]
    public async Task CreateBrebPaymentAsync_MissingId_ThrowsProtocolException()
    {
        const string body = """{ "status": "PROCESSING" }""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client  = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(() => client.CreateBrebPaymentAsync(SyntheticRequest()));
    }

    [Fact]
    public async Task CreateBrebPaymentAsync_Http400_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"error_code":"U111"}"""));
        var client  = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.CreateBrebPaymentAsync(SyntheticRequest()));
    }

    [Fact]
    public async Task CreateBrebPaymentAsync_Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));
        var client  = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(() => client.CreateBrebPaymentAsync(SyntheticRequest()));
    }

    [Fact]
    public async Task CreateBrebPaymentAsync_NullRequest_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CreateBrebPaymentAsync(null!));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateBrebPaymentAsync_MissingAccountId_ThrowsBeforeHttp(string? accountId)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);
        var request = SyntheticRequest() with { AccountId = accountId! };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateBrebPaymentAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateBrebPaymentAsync_MissingResolutionId_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);
        var request = SyntheticRequest() with { ResolutionId = "" };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateBrebPaymentAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateBrebPaymentAsync_WrongCurrency_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);
        var request = SyntheticRequest() with { Amount = new PassportPaymentAmountRequest("5000.00", "USD") };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateBrebPaymentAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    // ── RetrievePaymentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RetrievePaymentAsync_GetsToExactContractPath()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);

        await client.RetrievePaymentAsync("synthetic-payment-id-001");

        Assert.Equal($"{BaseUrl}/v1/payments/synthetic-payment-id-001", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
    }

    [Fact]
    public async Task RetrievePaymentAsync_RejectedResponse_DeserializesErrorObject()
    {
        const string body = """
            {
              "id": "synthetic-payment-id-001",
              "status": "REJECTED",
              "account_id": "synthetic-operational-account-id-001",
              "resolution_id": "synthetic-resolution-id-001",
              "amount": { "value": "100000", "currency": "COP" },
              "error": { "error_code": "B002", "error_description": "B002" }
            }
            """;
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client  = CreateClient(handler);

        var result = await client.RetrievePaymentAsync("synthetic-payment-id-001");

        Assert.Equal("REJECTED", result.Status);
        Assert.Equal("B002", result.Error?.ErrorCode);
    }

    [Fact]
    public async Task RetrievePaymentAsync_MissingPaymentId_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullPaymentResponseBody));
        var client  = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.RetrievePaymentAsync(""));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RetrievePaymentAsync_MissingId_ThrowsProtocolException()
    {
        const string body = """{ "status": "SETTLED" }""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client  = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(() => client.RetrievePaymentAsync("synthetic-payment-id-001"));
    }

    [Fact]
    public async Task RetrievePaymentAsync_Http500_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        var client  = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.RetrievePaymentAsync("synthetic-payment-id-001"));
    }
}
