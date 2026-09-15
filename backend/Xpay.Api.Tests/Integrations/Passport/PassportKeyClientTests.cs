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

    // ════════════════════════════════════════════════════════════════════
    // XPAY-293 — Suspend / Activate / Delete Key (contrato confirmado XPAY-292)
    // ════════════════════════════════════════════════════════════════════

    // ── SuspendKeyAsync ──────────────────────────────────────────────────

    // 1/2/3 — PATCH exacto a /v1/keys/{id}/suspend, sin body.
    [Fact]
    public async Task SuspendKeyAsync_UsesPatchToExactPathWithoutBody()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id-001\",\"status\":\"SUSPENDED\"}"));
        var client = CreateClient(handler);

        await client.SuspendKeyAsync("synthetic-key-id-001");

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/v1/keys/synthetic-key-id-001/suspend", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequestBody);
    }

    // 4/5 — response 200 se deserializa; id remoto se devuelve.
    [Fact]
    public async Task SuspendKeyAsync_Http200_DeserializesAndReturnsId()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-key-id-001",
                  "status": "SUSPENDED",
                  "account_id": "synthetic-account-id-001",
                  "key": { "key_type": "BCODE", "key_value": "0000000000" },
                  "created_at": "2026-01-01T00:00:00.00000Z",
                  "updated_at": "2026-01-02T00:00:00.00000Z"
                }
                """));
        var client = CreateClient(handler);

        var result = await client.SuspendKeyAsync("synthetic-key-id-001");

        Assert.Equal("synthetic-key-id-001", result.Id);
        Assert.Equal("SUSPENDED", result.Status);
    }

    // 6/7/8 — id remoto ausente/vacío/whitespace en la respuesta → guard.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"\"}")]
    [InlineData("{\"id\":\"   \"}")]
    public async Task SuspendKeyAsync_MissingOrBlankResponseId_ThrowsProtocolException(string body)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(() => client.SuspendKeyAsync("synthetic-key-id-001"));
        Assert.Equal(1, handler.CallCount);
    }

    // 9 — keyId de input vacío falla ANTES de HTTP.
    [Fact]
    public async Task SuspendKeyAsync_BlankKeyIdInput_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"x\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SuspendKeyAsync("   "));
        Assert.Equal(0, handler.CallCount);
    }

    // 21 — path safety: un keyId sintético con un carácter que debe
    // escaparse ('/') no puede alterar la ruta lógica.
    [Fact]
    public async Task SuspendKeyAsync_KeyIdWithSlash_IsEscapedAndDoesNotAlterPath()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"whatever\"}"));
        var client = CreateClient(handler);

        await client.SuspendKeyAsync("synthetic/../evil-id");

        // El '/' debe llegar percent-encoded (%2F): la ruta NO debe
        // interpretarse como múltiples segmentos ni alterar /suspend.
        Assert.EndsWith("/suspend", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("%2F", handler.LastRequest.RequestUri.AbsoluteUri);
        Assert.DoesNotContain("/v1/keys/synthetic/../evil-id/suspend", handler.LastRequest.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task SuspendKeyAsync_Http404_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.SuspendKeyAsync("synthetic-key-id-001"));
    }

    // ── ActivateKeyAsync ─────────────────────────────────────────────────

    // 10/11/12 — PATCH exacto a /v1/keys/{id}/activate, sin body.
    [Fact]
    public async Task ActivateKeyAsync_UsesPatchToExactPathWithoutBody()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id-001\",\"status\":\"ACTIVE\"}"));
        var client = CreateClient(handler);

        await client.ActivateKeyAsync("synthetic-key-id-001");

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/v1/keys/synthetic-key-id-001/activate", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequestBody);
    }

    // 13 — response 200 se deserializa.
    [Fact]
    public async Task ActivateKeyAsync_Http200_DeserializesAndReturnsId()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-key-id-001\",\"status\":\"ACTIVE\"}"));
        var client = CreateClient(handler);

        var result = await client.ActivateKeyAsync("synthetic-key-id-001");

        Assert.Equal("synthetic-key-id-001", result.Id);
        Assert.Equal("ACTIVE", result.Status);
    }

    // 14 — remote-id guard equivalente al de Suspend/Create.
    [Fact]
    public async Task ActivateKeyAsync_MissingResponseId_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(() => client.ActivateKeyAsync("synthetic-key-id-001"));
        Assert.Equal(1, handler.CallCount);
    }

    // 15 — keyId inválido falla antes de HTTP.
    [Fact]
    public async Task ActivateKeyAsync_BlankKeyIdInput_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"x\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ActivateKeyAsync(""));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ActivateKeyAsync_Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(() => client.ActivateKeyAsync("synthetic-key-id-001"));
    }

    // ── DeleteKeyAsync ───────────────────────────────────────────────────

    // 16/17 — DELETE exacto a /v1/keys/{id}.
    [Fact]
    public async Task DeleteKeyAsync_UsesDeleteToExactPath()
    {
        var handler = new FakeHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler);

        await client.DeleteKeyAsync("synthetic-key-id-001");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("/v1/keys/synthetic-key-id-001", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequestBody);
    }

    // 18/19 — 204 completa exitosamente sin requerir/leer response body.
    [Fact]
    public async Task DeleteKeyAsync_204NoContent_CompletesWithoutResponseBody()
    {
        var handler = new FakeHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler);

        await client.DeleteKeyAsync("synthetic-key-id-001");

        Assert.Equal(1, handler.CallCount);
    }

    // 20 — keyId inválido falla antes de HTTP.
    [Fact]
    public async Task DeleteKeyAsync_BlankKeyIdInput_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.DeleteKeyAsync(null!));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DeleteKeyAsync_Http500_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "boom"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.DeleteKeyAsync("synthetic-key-id-001"));
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-324 — ResolveKeyAsync (POST /v1/resolve-key)
    // ══════════════════════════════════════════════════════════════════════

    private static PassportResolveKeyRequest SyntheticResolveRequest(
        string customerId = "synthetic-customer-id-001",
        PassportKeyType keyType = PassportKeyType.BCODE,
        string keyValue = "0000000000") =>
        new(CustomerId: customerId, Key: new PassportKeyRequest(keyType, keyValue));

    // Ejemplo sintético construido con exactamente el shape confirmado en el
    // diagnóstico previo (estructura real observada en Sandbox): ningún
    // valor real de un customer/cuenta/BCODE, ningún dato de un titular real.
    private const string FullResolveKeyResponseBody = """
        {
          "id": "synthetic-resolution-id-001",
          "receptor_node": "SYNTH-NODE",
          "resolved_at": "2026-01-01T00:00:00.000000Z",
          "expires_at": "2026-01-01T00:30:00.000000Z",
          "customer_id": "synthetic-customer-id-001",
          "owner": {
            "first_name": "Synthetic",
            "second_name": "Test",
            "first_last_name": "Owner",
            "second_last_name": "Fixture",
            "business_name": null,
            "identification_type": "CC",
            "identification_number": "0000000000",
            "type": "PERSON"
          },
          "key": { "key_type": "BCODE", "key_value": "0000000000" },
          "participant": { "name": "Synthetic Participant", "identification_number": "1111111111" },
          "account": { "account_number": "2222222222", "account_type": "SAVINGS" }
        }
        """;

    // ── Request happy path ───────────────────────────────────────────────

    [Fact]
    public async Task ResolveKeyAsync_PostsToExactContractPath()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler);

        await client.ResolveKeyAsync(SyntheticResolveRequest());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/resolve-key", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
    }

    [Fact]
    public async Task ResolveKeyAsync_SendsBearerFromExistingInfrastructure()
    {
        var tokenProvider = new FakePassportTokenProvider("synthetic-bearer-token");
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler, tokenProvider);

        await client.ResolveKeyAsync(SyntheticResolveRequest());

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("synthetic-bearer-token", handler.LastRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ResolveKeyAsync_SerializesRequestWithExactContractShape()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler);

        await client.ResolveKeyAsync(SyntheticResolveRequest());

        using var doc = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("synthetic-customer-id-001", root.GetProperty("customer_id").GetString());
        var key = root.GetProperty("key");
        Assert.Equal("BCODE", key.GetProperty("key_type").GetString());
        Assert.Equal("0000000000", key.GetProperty("key_value").GetString());

        // Sin campos inventados: exactamente 2 propiedades top-level.
        var topLevelNames = new List<string>();
        foreach (var prop in root.EnumerateObject())
            topLevelNames.Add(prop.Name);
        Assert.Equal(new[] { "customer_id", "key" }, topLevelNames);
    }

    // ── Input guards — ninguno debe llegar a HTTP ───────────────────────

    [Fact]
    public async Task ResolveKeyAsync_NullRequest_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ResolveKeyAsync(null!));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveKeyAsync_MissingCustomerId_ThrowsBeforeHttp(string? customerId)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ResolveKeyAsync(SyntheticResolveRequest(customerId: customerId!)));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ResolveKeyAsync_NullKey_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler);

        var request = new PassportResolveKeyRequest(CustomerId: "synthetic-customer-id-001", Key: null!);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ResolveKeyAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ResolveKeyAsync_OutOfRangeKeyType_ThrowsBeforeHttp()
    {
        // XPAY-289/324 — mismo hallazgo de Create Key: un cast fuera de
        // rango no es rechazado por el compilador ni por
        // JsonStringEnumConverter al serializar; Enum.IsDefined debe
        // rechazarlo ANTES de HTTP.
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler);

        var request = SyntheticResolveRequest(keyType: (PassportKeyType)999);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ResolveKeyAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveKeyAsync_MissingKeyValue_ThrowsBeforeHttp(string? keyValue)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ResolveKeyAsync(SyntheticResolveRequest(keyValue: keyValue!)));
        Assert.Equal(0, handler.CallCount);
    }

    // ── Response deserialization — shape anidado completo ───────────────

    [Fact]
    public async Task ResolveKeyAsync_Http200_DeserializesFullNestedShape()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullResolveKeyResponseBody));
        var client = CreateClient(handler);

        var result = await client.ResolveKeyAsync(SyntheticResolveRequest());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("synthetic-resolution-id-001", result.Id);
        Assert.Equal("SYNTH-NODE", result.ReceptorNode);
        Assert.Equal("2026-01-01T00:00:00.000000Z", result.ResolvedAt);
        Assert.Equal("2026-01-01T00:30:00.000000Z", result.ExpiresAt);
        Assert.Equal("synthetic-customer-id-001", result.CustomerId);

        Assert.NotNull(result.Owner);
        Assert.Equal("Synthetic", result.Owner!.FirstName);
        Assert.Equal("Test", result.Owner.SecondName);
        Assert.Equal("Owner", result.Owner.FirstLastName);
        Assert.Equal("Fixture", result.Owner.SecondLastName);
        Assert.Null(result.Owner.BusinessName);
        Assert.Equal("CC", result.Owner.IdentificationType);
        Assert.Equal("0000000000", result.Owner.IdentificationNumber);
        Assert.Equal("PERSON", result.Owner.Type);

        // key reutiliza PassportKeyResponseDetail (exact match confirmado).
        Assert.NotNull(result.Key);
        Assert.Equal("BCODE", result.Key!.KeyType);
        Assert.Equal("0000000000", result.Key.KeyValue);

        Assert.NotNull(result.Participant);
        Assert.Equal("Synthetic Participant", result.Participant!.Name);
        Assert.Equal("1111111111", result.Participant.IdentificationNumber);

        Assert.NotNull(result.Account);
        Assert.Equal("2222222222", result.Account!.AccountNumber);
        Assert.Equal("SAVINGS", result.Account.AccountType);
    }

    [Fact]
    public async Task ResolveKeyAsync_MissingId_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(() => client.ResolveKeyAsync(SyntheticResolveRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    // ── Error route — reutiliza el manejo genérico existente ────────────

    [Fact]
    public async Task ResolveKeyAsync_Http400_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"bad_request\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.ResolveKeyAsync(SyntheticResolveRequest()));
    }

    [Fact]
    public async Task ResolveKeyAsync_Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(() => client.ResolveKeyAsync(SyntheticResolveRequest()));
    }

    [Fact]
    public async Task ResolveKeyAsync_MissingId_ExceptionMessageIsSanitized()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.ResolveKeyAsync(SyntheticResolveRequest()));

        Assert.DoesNotContain("synthetic-customer-id-001", ex.Message);
        Assert.DoesNotContain("synthetic-bearer-token", ex.Message);
        Assert.DoesNotContain("test-client-secret", ex.Message);
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-328 — ListKeysAsync (GET /v1/keys), contrato confirmado XPAY-327
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticListAccountId = "synthetic-account-id-001";
    private const string SyntheticListKeyValue   = "0000000000";

    private static string SyntheticSingleKeyResponseBody(
        string id = "synthetic-key-id-001",
        string accountId = SyntheticListAccountId,
        string keyType = "BCODE",
        string keyValue = SyntheticListKeyValue) => $$"""
        {
          "keys": [
            {
              "id": "{{id}}",
              "status": "ACTIVE",
              "key": { "key_type": "{{keyType}}", "key_value": "{{keyValue}}" },
              "account_id": "{{accountId}}",
              "created_at": "2026-01-01T00:00:00.000Z",
              "updated_at": "2026-01-01T00:00:00.000Z"
            }
          ],
          "pagination_info": {
            "first_request_timestamp": "2026-01-01T00:00:00.000Z",
            "current_page": 1,
            "total_pages": 1,
            "total_elements": 1
          }
        }
        """;

    // A — GET exacto a /v1/keys, con account_id/key_type/key_value en el query.
    [Fact]
    public async Task ListKeysAsync_GetsExactPathWithFiltersInQuery()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SyntheticSingleKeyResponseBody()));
        var client = CreateClient(handler);

        await client.ListKeysAsync(SyntheticListAccountId, PassportKeyType.BCODE, SyntheticListKeyValue);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/v1/keys", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));

        var query = handler.LastRequest.RequestUri.Query;
        Assert.Contains($"account_id={SyntheticListAccountId}", query);
        Assert.Contains("key_type=BCODE", query);
        Assert.Contains($"key_value={SyntheticListKeyValue}", query);
        Assert.Null(handler.LastRequestBody);
    }

    // B — URL encoding: valores sintéticos con caracteres reservados quedan
    // correctamente codificados (nunca concatenación insegura).
    [Fact]
    public async Task ListKeysAsync_EncodesReservedCharactersInQueryValues()
    {
        const string accountIdWithReserved = "synthetic account&id=002";
        const string keyValueWithReserved   = "synthetic+value@example.test";

        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SyntheticSingleKeyResponseBody()));
        var client = CreateClient(handler);

        await client.ListKeysAsync(accountIdWithReserved, PassportKeyType.EMAIL, keyValueWithReserved);

        var uri = handler.LastRequest!.RequestUri!;
        // El espacio/'&'/'='/'+'/'@' NUNCA deben llegar crudos partiendo el
        // query string en parámetros no intencionados.
        Assert.Contains(Uri.EscapeDataString(accountIdWithReserved), uri.Query);
        Assert.Contains(Uri.EscapeDataString(keyValueWithReserved), uri.Query);
        Assert.DoesNotContain("synthetic account&id=002", uri.Query);

        // El servidor (fake) recibe exactamente 3 parámetros — el '&'/'='
        // embebidos y escapados no crean un cuarto parámetro espurio.
        var pairs = uri.Query.TrimStart('?').Split('&');
        Assert.Equal(3, pairs.Length);
    }

    // C — response con exactamente 1 key: se conservan todos los campos ya
    // soportados por PassportKeyResponse.
    [Fact]
    public async Task ListKeysAsync_Http200_DeserializesSingleKeyWithAllFields()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SyntheticSingleKeyResponseBody()));
        var client = CreateClient(handler);

        var result = await client.ListKeysAsync(SyntheticListAccountId, PassportKeyType.BCODE, SyntheticListKeyValue);

        Assert.Single(result.Keys);
        var key = result.Keys[0];
        Assert.Equal("synthetic-key-id-001", key.Id);
        Assert.Equal("ACTIVE", key.Status);
        Assert.NotNull(key.Key);
        Assert.Equal("BCODE", key.Key!.KeyType);
        Assert.Equal(SyntheticListKeyValue, key.Key.KeyValue);
        Assert.Equal(SyntheticListAccountId, key.AccountId);

        Assert.NotNull(result.PaginationInfo);
        Assert.Equal(1, result.PaginationInfo!.TotalElements);
    }

    // D — keys=[] se acepta correctamente: NO es un fallo de protocolo (a
    // diferencia de Create/Suspend/Resolve, donde `id` ausente sí lo es).
    [Fact]
    public async Task ListKeysAsync_EmptyKeysArray_ReturnsEmptyListWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{ "keys": [] }"""));
        var client = CreateClient(handler);

        var result = await client.ListKeysAsync(SyntheticListAccountId, PassportKeyType.BCODE, SyntheticListKeyValue);

        Assert.Empty(result.Keys);
        Assert.Equal(1, handler.CallCount);
    }

    // E — account_id vacío falla ANTES de HTTP.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListKeysAsync_BlankAccountId_ThrowsBeforeHttp(string? accountId)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SyntheticSingleKeyResponseBody()));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ListKeysAsync(accountId!, PassportKeyType.BCODE, SyntheticListKeyValue));
        Assert.Equal(0, handler.CallCount);
    }

    // F — key_value vacío falla ANTES de HTTP.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListKeysAsync_BlankKeyValue_ThrowsBeforeHttp(string? keyValue)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SyntheticSingleKeyResponseBody()));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ListKeysAsync(SyntheticListAccountId, PassportKeyType.BCODE, keyValue!));
        Assert.Equal(0, handler.CallCount);
    }

    // G — key_type fuera de rango (cast explícito) falla ANTES de HTTP.
    [Fact]
    public async Task ListKeysAsync_OutOfRangeKeyType_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SyntheticSingleKeyResponseBody()));
        var client = CreateClient(handler);
        var invalidKeyType = (PassportKeyType)999;

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => client.ListKeysAsync(SyntheticListAccountId, invalidKeyType, SyntheticListKeyValue));

        Assert.Equal(0, handler.CallCount);
        Assert.DoesNotContain(SyntheticListAccountId, ex.Message);
        Assert.DoesNotContain(SyntheticListKeyValue, ex.Message);
    }

    // H — "MOBILE" no es un PassportKeyType válido en C#: no hay forma de
    // invocar ListKeysAsync con un literal "MOBILE" (el compilador ya lo
    // impide). Se reconfirma aquí, en el contexto de List Keys, que el enum
    // sigue sin definir MOBILE — mismo hallazgo que CreateKeyAsync.
    [Fact]
    public void PassportKeyType_DoesNotDefineMobile_ForListKeys()
    {
        Assert.DoesNotContain("MOBILE", Enum.GetNames<PassportKeyType>());
    }

    // I — respuesta de error: mismo comportamiento genérico ya existente de
    // PassportHttpClient (sin lógica especial para List Keys).
    [Fact]
    public async Task ListKeysAsync_Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(
            () => client.ListKeysAsync(SyntheticListAccountId, PassportKeyType.BCODE, SyntheticListKeyValue));
    }

    [Fact]
    public async Task ListKeysAsync_Http500_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "boom"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(
            () => client.ListKeysAsync(SyntheticListAccountId, PassportKeyType.BCODE, SyntheticListKeyValue));
    }

    [Fact]
    public async Task ListKeysAsync_MalformedJson_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{ not valid json"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.ListKeysAsync(SyntheticListAccountId, PassportKeyType.BCODE, SyntheticListKeyValue));
    }

    // Confirma que ningún dato sintético ni el Bearer aparecen en un mensaje
    // de excepción de error HTTP (mismo criterio de saneamiento ya aplicado
    // en Create/Resolve Key).
    [Fact]
    public async Task ListKeysAsync_Http400_ExceptionMessageIsSanitized()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"bad_request\"}"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.ListKeysAsync(SyntheticListAccountId, PassportKeyType.BCODE, SyntheticListKeyValue));

        Assert.DoesNotContain(SyntheticListAccountId, ex.Message);
        Assert.DoesNotContain(SyntheticListKeyValue, ex.Message);
        Assert.DoesNotContain("synthetic-bearer-token", ex.Message);
        Assert.DoesNotContain("test-client-secret", ex.Message);
    }
}
