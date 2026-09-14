using System.Net;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-272 — unit tests del cliente HTTP base Passport. SIN red real y SIN
// ningún endpoint de negocio: sólo prueba el mecanismo genérico de envío
// autenticado (Bearer + BaseUrl + JSON + cancellation), usando tipos
// sintéticos de request/response propios de este archivo de test.
public class PassportHttpClientTests
{
    private const string BaseUrl = "https://passport.test";

    private sealed record SyntheticRequest(string Value);
    private sealed record SyntheticResponse(string Echo);

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [PassportOptions.EnvBaseUrl]      = BaseUrl,
        [PassportOptions.EnvClientId]     = "test-client-id",
        [PassportOptions.EnvClientSecret] = "test-client-secret",
    };

    private static PassportHttpClient CreateClient(
        FakeHttpMessageHandler handler,
        Dictionary<string, string?> config,
        FakePassportTokenProvider? tokenProvider = null)
        => new(
            new FakeHttpClientFactory(handler),
            tokenProvider ?? new FakePassportTokenProvider(),
            new FakeConfiguration(config),
            new CapturingLogger<PassportHttpClient>());

    [Fact]
    public async Task PostAsync_SendsBearerTokenAndResolvesAgainstBaseUrl()
    {
        var tokenProvider = new FakePassportTokenProvider("obtained-token");
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"echo\":\"ok\"}"));
        var client = CreateClient(handler, ValidConfig(), tokenProvider);

        var result = await client.PostAsync<SyntheticRequest, SyntheticResponse>(
            "/v1/some-future-endpoint", new SyntheticRequest("x"));

        Assert.NotNull(result);
        Assert.Equal("ok", result!.Echo);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, tokenProvider.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/some-future-endpoint", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("obtained-token", handler.LastRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetAsync_SendsBearerTokenWithoutBody()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"echo\":\"got-it\"}"));
        var client = CreateClient(handler, ValidConfig());

        var result = await client.GetAsync<SyntheticResponse>("/v1/some-future-query");

        Assert.NotNull(result);
        Assert.Equal("got-it", result!.Echo);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Null(handler.LastRequestBody);
        Assert.NotNull(handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task MissingBaseUrl_FailsBeforeAnyHttpCallOrTokenRequest()
    {
        var tokenProvider = new FakePassportTokenProvider();
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"echo\":\"ok\"}"));
        var config = new Dictionary<string, string?>(); // sin BaseUrl
        var client = CreateClient(handler, config, tokenProvider);

        await Assert.ThrowsAsync<PassportConfigurationException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, tokenProvider.CallCount); // fail-closed ANTES de pedir token
    }

    [Fact]
    public async Task TokenProviderFailure_PropagatesWithoutHttpCall()
    {
        var tokenProvider = new FakePassportTokenProvider(
            toThrow: new PassportConfigurationException("Configuración de Passport incompleta."));
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"echo\":\"ok\"}"));
        var client = CreateClient(handler, ValidConfig(), tokenProvider);

        await Assert.ThrowsAsync<PassportConfigurationException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var client = CreateClient(handler, ValidConfig());

        await Assert.ThrowsAsync<PassportAuthenticationException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));
    }

    [Fact]
    public async Task PreCancelledToken_PropagatesCancellationWithoutHttpCall()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"echo\":\"ok\"}"));
        var client = CreateClient(handler, ValidConfig());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>(
                "/v1/x", new SyntheticRequest("x"), cts.Token));
    }
}
