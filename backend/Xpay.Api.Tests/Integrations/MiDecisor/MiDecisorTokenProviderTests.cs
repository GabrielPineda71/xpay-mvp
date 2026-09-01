using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xpay.Api.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

// M2.1 — unit tests del token provider. SIN red real: todo pasa por un
// FakeHttpMessageHandler en memoria. Los valores "test-*" son sintéticos,
// no son credenciales reales y nunca se imprimen.
public class MiDecisorTokenProviderTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string BaseUrl      = "https://midecisor.test";
    private const string ExpectedPath = "/spla/oauth2/v1/token";

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [MiDecisorOptions.EnvBaseUrl]      = BaseUrl,
        [MiDecisorOptions.EnvClientId]     = "test-client",
        [MiDecisorOptions.EnvClientSecret] = "test-secret",
        [MiDecisorOptions.EnvUsername]     = "test-user",
        [MiDecisorOptions.EnvPassword]     = "test-password",
    };

    private static MiDecisorTokenProvider CreateProvider(
        FakeHttpMessageHandler handler,
        Dictionary<string, string?> config,
        TimeProvider timeProvider)
        => new(
            new FakeHttpClientFactory(handler),
            new FakeConfiguration(config),
            NullLogger<MiDecisorTokenProvider>.Instance,
            timeProvider);

    private static string TokenBody(string token, string expiresIn)
        => $"{{\"access_token\":\"{token}\",\"expires_in\":\"{expiresIn}\"}}";

    // 1 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task FirstCall_AuthenticatesAndReturnsToken()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", "3600")));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("test-token", token);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(ExpectedPath, handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));

        Assert.True(handler.LastRequest.Headers.TryGetValues("Client_id", out var cid));
        Assert.Equal("test-client", Assert.Single(cid!));
        Assert.True(handler.LastRequest.Headers.TryGetValues("Client_secret", out var csec));
        Assert.Equal("test-secret", Assert.Single(csec!));

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"username\":\"test-user\"", handler.LastRequestBody!);
        Assert.Contains("\"password\":\"test-password\"", handler.LastRequestBody!);
        // El body NO debe llevar las credenciales de cliente.
        Assert.DoesNotContain("test-secret", handler.LastRequestBody!);
    }

    // 2 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SecondCallBeforeExpiry_UsesCache()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", "3600")));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        var first  = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("test-token", first);
        Assert.Equal("test-token", second);
        Assert.Equal(1, handler.CallCount);
    }

    // 3 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ExpiredToken_AuthenticatesAgain()
    {
        var responses = new Queue<string>(new[] { "token-1", "token-2" });
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody(responses.Dequeue(), "3600")));
        var clock = new TestTimeProvider(T0);
        var provider = CreateProvider(handler, ValidConfig(), clock);

        var first = await provider.GetAccessTokenAsync();
        // expires_in 3600 − margen 30 = 3570s efectivos; avanzamos más allá.
        clock.Advance(TimeSpan.FromSeconds(3571));
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.CallCount);
    }

    // 4 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task InvalidExpiresIn_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", "soon")));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => provider.GetAccessTokenAsync());

        // Nada quedó cacheado: una segunda llamada vuelve a intentar el HTTP.
        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(2, handler.CallCount);
    }

    // 5 ─────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    public async Task NonPositiveExpiresIn_ThrowsProtocolException(string expiresIn)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", expiresIn)));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => provider.GetAccessTokenAsync());
    }

    // 6 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task MissingAccessToken_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"expires_in\":\"3600\"}"));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => provider.GetAccessTokenAsync());
    }

    // 7 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task MalformedSuccessBody_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "no-json-aqui"));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => provider.GetAccessTokenAsync());
    }

    // 8 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Auth401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}"));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorAuthenticationException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(1, handler.CallCount);
    }

    // 9 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Auth500_FailsWithoutRetry()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "boom"));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorTransportException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(1, handler.CallCount);
    }

    // 10 ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task MissingConfiguration_FailsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", "3600")));
        var provider = CreateProvider(handler, new Dictionary<string, string?>(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorConfigurationException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(0, handler.CallCount);
    }

    // 11 ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConcurrentColdCache_AuthenticatesOnce()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(50, ct);
            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", "3600"));
        });
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => provider.GetAccessTokenAsync()));

        Assert.All(results, r => Assert.Equal("test-token", r));
        Assert.Equal(1, handler.CallCount);
    }

    // 12 ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PreCancelledToken_PropagatesCancellation()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", "3600")));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAccessTokenAsync(cts.Token));

        Assert.IsNotType<MiDecisorException>(ex);
        Assert.Equal(0, handler.CallCount);
    }

    // 14 ── FIX 1 (059 BLOCKER): timeout interno durante la lectura del body ─
    [Fact]
    public async Task BodyReadTimeout_ThrowsTransportExceptionWithoutRetry()
    {
        var config = ValidConfig();
        config[MiDecisorOptions.EnvTimeoutSeconds] = "1"; // timeout sintético pequeño
        var handler = new FakeHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingHttpContent() });
        var provider = CreateProvider(handler, config, new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorTransportException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(1, handler.CallCount); // sin retry
    }

    // 15 ── FIX 2 (059 SHOULD_FIX): BaseUrl con scheme no http/https ─────────
    [Fact]
    public async Task NonHttpBaseUrl_FailsBeforeHttp()
    {
        var config = ValidConfig();
        config[MiDecisorOptions.EnvBaseUrl] = "ftp://midecisor.test";
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", "3600")));
        var provider = CreateProvider(handler, config, new TestTimeProvider(T0));

        await Assert.ThrowsAsync<MiDecisorConfigurationException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(0, handler.CallCount);
    }

    // 16 ── FIX 3 (059 SHOULD_FIX): safety margin = 0 explícito y honrado ────
    [Fact]
    public void ZeroSafetyMargin_IsHonored()
    {
        var config = ValidConfig();
        config[MiDecisorOptions.EnvTokenSafetyMarginSeconds] = "0";

        var opts = MiDecisorOptions.FromConfiguration(new FakeConfiguration(config), out var warnings);

        Assert.Equal(0, opts.TokenSafetyMarginSeconds);
        Assert.DoesNotContain(MiDecisorOptions.EnvTokenSafetyMarginSeconds, warnings);
        Assert.Equal(MiDecisorOptions.DefaultTimeoutSeconds, opts.TimeoutSeconds);
    }

    // 13 ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SafetyMarginGreaterThanLifetime_DoesNotLoop()
    {
        var config = ValidConfig();
        config[MiDecisorOptions.EnvTokenSafetyMarginSeconds] = "100";
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", "10")));
        var provider = CreateProvider(handler, config, new TestTimeProvider(T0));

        var first = await provider.GetAccessTokenAsync();
        // Mismo instante: la ventana mínima de 1s sigue vigente => cache hit.
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("test-token", first);
        Assert.Equal("test-token", second);
        Assert.Equal(1, handler.CallCount);
    }
}

// IConfiguration mínimo para tests: sólo el indexador plano (el patrón que
// usa el provider, igual que KycService con las claves VERIFF_*). El resto
// de la interfaz no se usa aquí.
internal sealed class FakeConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public FakeConfiguration(Dictionary<string, string?> values) => _values = values;

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var v) ? v : null;
        set => _values[key] = value;
    }

    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    public IEnumerable<IConfigurationSection> GetChildren() => throw new NotSupportedException();
    public IChangeToken GetReloadToken() => throw new NotSupportedException();
}
