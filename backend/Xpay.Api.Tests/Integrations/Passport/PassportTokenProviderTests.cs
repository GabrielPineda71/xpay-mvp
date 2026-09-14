using System.Net;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-272 — unit tests del token provider Passport. SIN red real: todo
// pasa por un FakeHttpMessageHandler en memoria. Los valores "test-*" son
// sintéticos, no son credenciales reales y nunca se imprimen.
public class PassportTokenProviderTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string BaseUrl      = "https://passport.test";
    private const string ExpectedPath = "/v1/iam/oauth/tokens";

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [PassportOptions.EnvBaseUrl]      = BaseUrl,
        [PassportOptions.EnvClientId]     = "test-client-id",
        [PassportOptions.EnvClientSecret] = "test-client-secret",
    };

    private static PassportTokenProvider CreateProvider(
        FakeHttpMessageHandler handler,
        Dictionary<string, string?> config,
        TimeProvider timeProvider,
        CapturingLogger<PassportTokenProvider>? logger = null)
        => new(
            new FakeHttpClientFactory(handler),
            new FakeConfiguration(config),
            logger ?? new CapturingLogger<PassportTokenProvider>(),
            timeProvider);

    private static string TokenBody(string token, int expiresIn, string tokenType = "Bearer")
        => $"{{\"access_token\":\"{token}\",\"expires_in\":{expiresIn},\"token_type\":\"{tokenType}\"}}";

    // 1/2 ────────────────────────────────────────────────────────────────────
    // POST correcto a /v1/iam/oauth/tokens; body JSON con client_id,
    // client_secret, grant_type=client_credentials.
    [Fact]
    public async Task FirstCall_PostsCorrectBodyToOAuthEndpoint()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", 86400)));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("test-token", token);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(ExpectedPath, handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
        Assert.Equal("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"client_id\":\"test-client-id\"", handler.LastRequestBody!);
        Assert.Contains("\"client_secret\":\"test-client-secret\"", handler.LastRequestBody!);
        Assert.Contains("\"grant_type\":\"client_credentials\"", handler.LastRequestBody!);
        // Las credenciales NO van como headers (a diferencia de MiDecisor).
        Assert.False(handler.LastRequest.Headers.Contains("Client_id"));
        Assert.False(handler.LastRequest.Headers.Contains("Client_secret"));
    }

    // 3/4/5 ──────────────────────────────────────────────────────────────────
    // Response 200 se deserializa correctamente; expires_in=86400 se procesa;
    // token_type Bearer se procesa (no rompe la deserialización).
    [Fact]
    public async Task SuccessResponse_DeserializesExpiresInAndTokenType()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", 86400, "Bearer")));
        var clock    = new TestTimeProvider(T0);
        var provider = CreateProvider(handler, ValidConfig(), clock);

        var token = await provider.GetAccessTokenAsync();
        Assert.Equal("test-token", token);

        // expires_in=86400 con margen default (60s) => vigente hasta T0+86340s.
        clock.Advance(TimeSpan.FromSeconds(86339));
        var stillCached = await provider.GetAccessTokenAsync();
        Assert.Equal("test-token", stillCached);
        Assert.Equal(1, handler.CallCount); // sigue cacheado, sin nueva llamada

        clock.Advance(TimeSpan.FromSeconds(2));
        var refreshed = await provider.GetAccessTokenAsync();
        Assert.Equal("test-token", refreshed);
        Assert.Equal(2, handler.CallCount); // venció, nueva llamada
    }

    // 6 ──────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SecondConsumer_ReusesCacheWhileValid()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", 86400)));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        var first  = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("test-token", first);
        Assert.Equal("test-token", second);
        Assert.Equal(1, handler.CallCount);
    }

    // 7 ──────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ExpiredToken_TriggersNewClientCredentialsRequest()
    {
        var responses = new Queue<string>(new[] { "token-1", "token-2" });
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody(responses.Dequeue(), 3600)));
        var clock    = new TestTimeProvider(T0);
        var provider = CreateProvider(handler, ValidConfig(), clock);

        var first = await provider.GetAccessTokenAsync();
        // expires_in 3600 − margen 60 = 3540s efectivos; avanzamos más allá.
        clock.Advance(TimeSpan.FromSeconds(3541));
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.CallCount);

        // Sin refresh_token: la segunda petición es un client_credentials
        // nuevo e íntegro (mismo body de credenciales), no una llamada distinta.
        Assert.Contains("\"grant_type\":\"client_credentials\"", handler.LastRequestBody!);
    }

    // 8 ──────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConcurrentColdCache_AuthenticatesOnce()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(50, ct);
            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", 86400));
        });
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => provider.GetAccessTokenAsync()));

        Assert.All(results, r => Assert.Equal("test-token", r));
        Assert.Equal(1, handler.CallCount);
    }

    // 9/10 ───────────────────────────────────────────────────────────────────
    // ClientSecret y AccessToken no aparecen en logs ni en mensajes de
    // excepción generados por la implementación.
    [Fact]
    public async Task SuccessPath_NeverLogsSecretOrAccessToken()
    {
        var logger = new CapturingLogger<PassportTokenProvider>();
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("super-secret-token-value", 86400)));
        var config   = ValidConfig();
        var provider = CreateProvider(handler, config, new TestTimeProvider(T0), logger);

        await provider.GetAccessTokenAsync();

        Assert.DoesNotContain(logger.Messages, m => m.Contains("test-client-secret"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("super-secret-token-value"));
    }

    [Fact]
    public async Task AuthRejected_ExceptionAndLogsNeverContainSecretOrToken()
    {
        var logger = new CapturingLogger<PassportTokenProvider>();
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}"));
        var config   = ValidConfig();
        var provider = CreateProvider(handler, config, new TestTimeProvider(T0), logger);

        var ex = await Assert.ThrowsAsync<PassportAuthenticationException>(() => provider.GetAccessTokenAsync());

        Assert.DoesNotContain("test-client-secret", ex.Message);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("test-client-secret"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("test-client-id"));
    }

    // 11 ─────────────────────────────────────────────────────────────────────
    // Configuración incompleta falla ANTES de cualquier operación externa.
    [Theory]
    [MemberData(nameof(IncompleteConfigs))]
    public async Task MissingConfiguration_FailsBeforeHttp(Dictionary<string, string?> config)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", 86400)));
        var provider = CreateProvider(handler, config, new TestTimeProvider(T0));

        await Assert.ThrowsAsync<PassportConfigurationException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(0, handler.CallCount);
    }

    public static IEnumerable<object[]> IncompleteConfigs()
    {
        yield return new object[] { new Dictionary<string, string?>() }; // nada configurado
        yield return new object[]
        {
            new Dictionary<string, string?> { [PassportOptions.EnvClientId] = "x", [PassportOptions.EnvClientSecret] = "y" },
        }; // falta BaseUrl
        yield return new object[]
        {
            new Dictionary<string, string?> { [PassportOptions.EnvBaseUrl] = BaseUrl, [PassportOptions.EnvClientSecret] = "y" },
        }; // falta ClientId
        yield return new object[]
        {
            new Dictionary<string, string?> { [PassportOptions.EnvBaseUrl] = BaseUrl, [PassportOptions.EnvClientId] = "x" },
        }; // falta ClientSecret
    }

    // Adicional — protocolo: expires_in ausente/no numérico/<=0 es error de
    // protocolo, no una excepción no controlada.
    [Fact]
    public async Task MissingExpiresIn_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\"}"));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<PassportProtocolException>(() => provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task MissingAccessToken_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"expires_in\":86400}"));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<PassportProtocolException>(() => provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Auth500_FailsWithoutRetry()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "boom"));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        await Assert.ThrowsAsync<PassportTransportException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task PreCancelledToken_PropagatesCancellation()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", 86400)));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAccessTokenAsync(cts.Token));

        Assert.IsNotType<PassportException>(ex);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task NonHttpBaseUrl_FailsBeforeHttp()
    {
        var config = ValidConfig();
        config[PassportOptions.EnvBaseUrl] = "ftp://passport.test";
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenBody("test-token", 86400)));
        var provider = CreateProvider(handler, config, new TestTimeProvider(T0));

        await Assert.ThrowsAsync<PassportConfigurationException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(0, handler.CallCount);
    }

    // ────────────────────────────────────────────────────────────────────────
    // XPAY-274 — tests contractuales para cerrar el hallazgo XPAY-273.
    //
    // Evidencia contractual disponible de Passport (POST /v1/iam/oauth/tokens):
    // `scopes` y `roles` son ARRAY JSON — NO string delimitada por espacios
    // (esa forma es la convención OAuth2 GENÉRICA del campo singular `scope`,
    // que NO aplica al campo específico `scopes` documentado por Passport).
    // Estos tests fijan ese contrato con datos sintéticos representativos del
    // payload real documentado; ningún valor es una credencial real.
    // ────────────────────────────────────────────────────────────────────────

    // Payload sintético representativo del contrato Passport completo,
    // incluyendo TODOS los campos opcionales documentados.
    private const string SyntheticTokenId    = "5f2c9b1e-7a3d-4e6f-9c2a-1b8d4e0f6a3c";
    private const string SyntheticAccountId  = "8a1e4f2b-3c9d-4a7e-b1f0-6d2c8a9e5b4f";
    private const string SyntheticCreatedAt  = "2026-01-01T00:00:00Z";
    private const string RoleEntityClientCredentials = "entity.client_credentials";

    private static string FullDocumentedContractBody(string accessToken, int expiresIn) => $$"""
        {
          "expires_in": {{expiresIn}},
          "access_token": "{{accessToken}}",
          "token_id": "{{SyntheticTokenId}}",
          "token_type": "Bearer",
          "scopes": ["breb.payments.write", "breb.keys.read"],
          "account_id": "{{SyntheticAccountId}}",
          "created_at": "{{SyntheticCreatedAt}}",
          "roles": ["{{RoleEntityClientCredentials}}"]
        }
        """;

    // TEST A — el payload documentado completo se deserializa correctamente
    // y GetAccessTokenAsync devuelve el access_token sintético esperado.
    [Fact]
    public async Task DocumentedFullContractPayload_Deserializes_AndReturnsAccessToken()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, FullDocumentedContractBody("synthetic-full-contract-token", 86400)));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("synthetic-full-contract-token", token);
        Assert.Equal(1, handler.CallCount);
    }

    // TEST B — el DTO PassportOAuthTokenResponse (público, sin acoplar
    // IPassportTokenProvider a su forma interna) deserializa correctamente
    // scopes/roles como arrays, con los valores enviados presentes.
    [Fact]
    public void DocumentedFullContractPayload_DtoDeserializesScopesAndRolesAsArrays()
    {
        var json = FullDocumentedContractBody("synthetic-full-contract-token", 86400);
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var dto = System.Text.Json.JsonSerializer.Deserialize<PassportOAuthTokenResponse>(json, opts);

        Assert.NotNull(dto);
        Assert.Equal("synthetic-full-contract-token", dto!.AccessToken);
        Assert.Equal(86400, dto.ExpiresIn);
        Assert.Equal("Bearer", dto.TokenType);
        Assert.Equal(SyntheticTokenId, dto.TokenId);
        Assert.Equal(SyntheticAccountId, dto.AccountId);
        Assert.Equal(SyntheticCreatedAt, dto.CreatedAt);

        Assert.NotNull(dto.Scopes);
        Assert.Contains("breb.payments.write", dto.Scopes!);
        Assert.Contains("breb.keys.read", dto.Scopes!);

        Assert.NotNull(dto.Roles);
        Assert.Contains(RoleEntityClientCredentials, dto.Roles!);
    }

    // TEST C — los campos opcionales pueden faltar COMPLETAMENTE (token_id,
    // scopes, account_id, created_at, roles ausentes) sin romper la
    // obtención del token, manteniendo únicamente los campos necesarios
    // para autenticación (access_token, expires_in, token_type).
    [Fact]
    public async Task MinimalContractPayload_WithoutOptionalFields_StillReturnsAccessToken()
    {
        var minimalBody = "{\"access_token\":\"synthetic-minimal-token\",\"expires_in\":86400,\"token_type\":\"Bearer\"}";
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, minimalBody));
        var provider = CreateProvider(handler, ValidConfig(), new TestTimeProvider(T0));

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("synthetic-minimal-token", token);
        Assert.Equal(1, handler.CallCount);
    }
}
