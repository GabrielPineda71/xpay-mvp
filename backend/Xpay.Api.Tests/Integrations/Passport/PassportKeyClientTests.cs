using System.Net;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-287 — unit tests de PassportKeyClient. SIN red real: se construye el
// PassportHttpClient de producción (XPAY-272) sobre un FakeHttpMessageHandler
// + FakePassportTokenProvider, exactamente igual al patrón ya usado en
// PassportCustomerAccountClientTests — ejercita la cadena completa (Bearer +
// BaseUrl + JSON), no un doble de IPassportHttpClient. Todos los valores son
// sintéticos: NO se usan cédulas, teléfonos ni emails reales.
public class PassportKeyClientTests
{
    private const string BaseUrl = "https://passport.test";

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [PassportOptions.EnvBaseUrl]      = BaseUrl,
        [PassportOptions.EnvClientId]     = "test-client-id",
        [PassportOptions.EnvClientSecret] = "test-client-secret",
    };

    private static PassportKeyClient CreateClient(
        FakeHttpMessageHandler handler,
        FakePassportTokenProvider? tokenProvider = null)
    {
        var http = new PassportHttpClient(
            new FakeHttpClientFactory(handler),
            tokenProvider ?? new FakePassportTokenProvider("synthetic-bearer-token"),
            new FakeConfiguration(ValidConfig()),
            new CapturingLogger<PassportHttpClient>());
        return new PassportKeyClient(http);
    }

    private static PassportCreateKeyRequest SyntheticRequest(
        PassportKeyType keyType = PassportKeyType.BCODE,
        string keyValue = "0000000000",
        string? displayName = "Synthetic Test Payer") => new(
        AccountId: "synthetic-account-id-001",
        Key: new PassportKeyRequest(keyType, keyValue))
    {
        DisplayName = displayName,
    };

    // 1/2 — POST exacto a /v1/keys.
    [Fact]
    public async Task CreateKeyAsync_PostsToExactContractPath()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id\"}"));
        var client = CreateClient(handler);

        await client.CreateKeyAsync(SyntheticRequest());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/keys", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
    }

    // 2 (complemento) — Bearer delegado a la infraestructura existente, sin
    // duplicar la cobertura de OAuth ya probada en PassportTokenProviderTests.
    [Fact]
    public async Task CreateKeyAsync_SendsBearerFromExistingInfrastructure()
    {
        var tokenProvider = new FakePassportTokenProvider("synthetic-bearer-token");
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id\"}"));
        var client = CreateClient(handler, tokenProvider);

        await client.CreateKeyAsync(SyntheticRequest());

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("synthetic-bearer-token", handler.LastRequest.Headers.Authorization!.Parameter);
        Assert.Equal(1, tokenProvider.CallCount);
    }

    // 3/4 — request JSON anidado bajo "key"; display_name presente.
    [Fact]
    public async Task CreateKeyAsync_SerializesNestedKeyAndDisplayNameWhenPresent()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id\"}"));
        var client = CreateClient(handler);

        await client.CreateKeyAsync(SyntheticRequest(PassportKeyType.EMAIL, "synthetic@example-sandbox.test", "Synthetic Payer"));

        using var doc = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("synthetic-account-id-001", root.GetProperty("account_id").GetString());
        Assert.Equal("Synthetic Payer", root.GetProperty("display_name").GetString());

        var key = root.GetProperty("key");
        Assert.Equal("EMAIL", key.GetProperty("key_type").GetString());
        Assert.Equal("synthetic@example-sandbox.test", key.GetProperty("key_value").GetString());
    }

    // 5 — display_name ausente se omite del JSON (no se envía como null).
    [Fact]
    public async Task CreateKeyAsync_OmitsDisplayNameWhenAbsent()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id\"}"));
        var client = CreateClient(handler);

        await client.CreateKeyAsync(SyntheticRequest(displayName: null));

        Assert.DoesNotContain("display_name", handler.LastRequestBody!);
    }

    // 6 — cada valor canónico del enum serializa exactamente con ese literal.
    [Theory]
    [InlineData(PassportKeyType.ID, "ID")]
    [InlineData(PassportKeyType.PHONE, "PHONE")]
    [InlineData(PassportKeyType.EMAIL, "EMAIL")]
    [InlineData(PassportKeyType.ALPHA, "ALPHA")]
    [InlineData(PassportKeyType.BCODE, "BCODE")]
    public async Task CreateKeyAsync_SerializesEachCanonicalKeyTypeLiteral(PassportKeyType keyType, string expectedLiteral)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id\"}"));
        var client = CreateClient(handler);

        await client.CreateKeyAsync(SyntheticRequest(keyType, "synthetic-value"));

        using var doc = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(expectedLiteral, doc.RootElement.GetProperty("key").GetProperty("key_type").GetString());
    }

    // Confirma explícitamente que "MOBILE" NUNCA aparece — no se introdujo
    // por error (XGOV histórico: Create Key documentaba "MOBILE" en la
    // página desactualizada; XPAY-286 confirmó "PHONE" es lo canónico).
    [Fact]
    public void PassportKeyType_DoesNotDefineMobile()
    {
        Assert.DoesNotContain("MOBILE", Enum.GetNames<PassportKeyType>());
    }

    // 7/8 — response 200 válida deserializa; id remoto retornado correctamente.
    [Fact]
    public async Task CreateKeyAsync_Http200_DeserializesIdStatusAndKey()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-key-id-001",
                  "status": "ACTIVE",
                  "key": { "key_type": "BCODE", "key_value": "0000000000" },
                  "display_name": "Synthetic Payer",
                  "account_id": "synthetic-account-id-001",
                  "created_at": "2026-01-01T00:00:00.00000Z",
                  "updated_at": "2026-01-01T00:00:00.00000Z"
                }
                """));
        var client = CreateClient(handler);

        var result = await client.CreateKeyAsync(SyntheticRequest());

        Assert.Equal("synthetic-key-id-001", result.Id);
        Assert.Equal("ACTIVE", result.Status);
        Assert.NotNull(result.Key);
        Assert.Equal("BCODE", result.Key!.KeyType);
        Assert.Equal("0000000000", result.Key.KeyValue);
        Assert.Equal("Synthetic Payer", result.DisplayName);
        Assert.Equal("synthetic-account-id-001", result.AccountId);
    }

    // 9/10/11 — id remoto obligatorio: ausente / "" / whitespace.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"\"}")]
    [InlineData("{\"id\":\"   \"}")]
    public async Task CreateKeyAsync_MissingOrBlankId_ThrowsProtocolException(string body)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(() => client.CreateKeyAsync(SyntheticRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    // 12/13/14/15 — errores documentados (400/401/403/500) se propagan según
    // el comportamiento genérico ya existente de PassportHttpClient.
    [Fact]
    public async Task CreateKeyAsync_Http400_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"bad_request\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.CreateKeyAsync(SyntheticRequest()));
    }

    [Fact]
    public async Task CreateKeyAsync_Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(() => client.CreateKeyAsync(SyntheticRequest()));
    }

    [Fact]
    public async Task CreateKeyAsync_Http403_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, "{\"error\":\"forbidden\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(() => client.CreateKeyAsync(SyntheticRequest()));
    }

    [Fact]
    public async Task CreateKeyAsync_Http500_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "boom"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.CreateKeyAsync(SyntheticRequest()));
    }

    // 16 — mensaje de excepción por missing id saneado: nunca incluye
    // account_id, key_value, display_name ni el token.
    [Fact]
    public async Task CreateKeyAsync_MissingId_ExceptionMessageIsSanitized()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.CreateKeyAsync(SyntheticRequest(PassportKeyType.EMAIL, "synthetic@example-sandbox.test", "Synthetic Payer")));

        Assert.DoesNotContain("synthetic-account-id-001", ex.Message);
        Assert.DoesNotContain("synthetic@example-sandbox.test", ex.Message);
        Assert.DoesNotContain("Synthetic Payer", ex.Message);
        Assert.DoesNotContain("synthetic-bearer-token", ex.Message);
        Assert.DoesNotContain("test-client-secret", ex.Message);
    }

    // ── Guards de input (Fase 7) ─────────────────────────────────────────

    [Fact]
    public async Task CreateKeyAsync_NullRequest_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"x\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CreateKeyAsync(null!));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateKeyAsync_BlankAccountId_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"x\"}"));
        var client = CreateClient(handler);
        var request = new PassportCreateKeyRequest("   ", new PassportKeyRequest(PassportKeyType.ID, "12345"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateKeyAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateKeyAsync_BlankKeyValue_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"x\"}"));
        var client = CreateClient(handler);
        var request = new PassportCreateKeyRequest("synthetic-account-id-001", new PassportKeyRequest(PassportKeyType.ID, ""));

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateKeyAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    // ── XPAY-289 — corrección hallazgo XPAY-288: key_type fuera de rango ────

    // 1/2 — un PassportKeyType construido fuera de los valores declarados
    // (cast explícito, no alcanzable escribiendo un literal del enum) lanza
    // ArgumentException ANTES de cualquier llamada HTTP — nunca llega a
    // serializarse ni a IPassportHttpClient.
    [Fact]
    public async Task CreateKeyAsync_OutOfRangeKeyType_ThrowsArgumentExceptionBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id\"}"));
        var client = CreateClient(handler);
        var invalidKeyType = (PassportKeyType)999;
        var request = new PassportCreateKeyRequest(
            "synthetic-account-id-001",
            new PassportKeyRequest(invalidKeyType, "synthetic-value"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.CreateKeyAsync(request));

        Assert.Equal(0, handler.CallCount);

        // 3 — mensaje saneado: no incluye key_value/account_id/token sintéticos.
        Assert.DoesNotContain("synthetic-value", ex.Message);
        Assert.DoesNotContain("synthetic-account-id-001", ex.Message);
        Assert.DoesNotContain("synthetic-bearer-token", ex.Message);
        Assert.DoesNotContain("test-client-secret", ex.Message);
    }

    // 4 — los 5 valores válidos existentes siguen funcionando sin cambios
    // (misma Theory que antes de la corrección, re-confirmada explícitamente
    // aquí como parte del cierre del hallazgo).
    [Theory]
    [InlineData(PassportKeyType.ID)]
    [InlineData(PassportKeyType.PHONE)]
    [InlineData(PassportKeyType.EMAIL)]
    [InlineData(PassportKeyType.ALPHA)]
    [InlineData(PassportKeyType.BCODE)]
    public async Task CreateKeyAsync_ValidKeyTypes_StillSucceedAfterGuard(PassportKeyType keyType)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id\"}"));
        var client = CreateClient(handler);

        var result = await client.CreateKeyAsync(SyntheticRequest(keyType, "synthetic-value"));

        Assert.Equal("synthetic-key-id", result.Id);
        Assert.Equal(1, handler.CallCount);
    }
}
