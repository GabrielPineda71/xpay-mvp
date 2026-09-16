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
        FakePassportTokenProvider? tokenProvider = null,
        CapturingLogger<PassportHttpClient>? logger = null)
        => new(
            new FakeHttpClientFactory(handler),
            tokenProvider ?? new FakePassportTokenProvider(),
            new FakeConfiguration(config),
            logger ?? new CapturingLogger<PassportHttpClient>());

    // XPAY-358 — helper compartido: confirma que NINGÚN marcador sintético
    // sensible aparece en ex.Message, ex.ToString() (que incluye stack
    // trace + Exception.Data, nunca propiedades custom, pero se verifica
    // igual por si acaso), ni en ningún mensaje efectivamente logueado.
    private static void AssertNoSensitiveLeak(
        Exception ex, CapturingLogger<PassportHttpClient> logger, params string[] sensitiveMarkers)
    {
        var exceptionToString = ex.ToString();
        foreach (var marker in sensitiveMarkers)
        {
            Assert.DoesNotContain(marker, ex.Message);
            Assert.DoesNotContain(marker, exceptionToString);
            foreach (var logMessage in logger.Messages)
                Assert.DoesNotContain(marker, logMessage);
        }
    }

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

    // ── XPAY-293 — PATCH genérico ────────────────────────────────────────

    [Fact]
    public async Task PatchAsync_SendsPatchWithBearerAndNoBody()
    {
        var tokenProvider = new FakePassportTokenProvider("obtained-token");
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"echo\":\"patched\"}"));
        var client = CreateClient(handler, ValidConfig(), tokenProvider);

        var result = await client.PatchAsync<SyntheticResponse>("/v1/some-future-endpoint/patch");

        Assert.NotNull(result);
        Assert.Equal("patched", result!.Echo);
        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/v1/some-future-endpoint/patch", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequestBody);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("obtained-token", handler.LastRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task PatchAsync_NonSuccessStatus_FollowsExistingErrorPattern()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"bad_request\"}"));
        var client = CreateClient(handler, ValidConfig());

        await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PatchAsync<SyntheticResponse>("/v1/x/patch"));
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-358 — diagnóstico HTTP seguro para respuestas de error (gap
    // identificado en XPAY-355: el body de un HTTP no exitoso se perdía por
    // completo). Cubre §11-15/19 del ticket: HTTP 400 con campos seguros,
    // JSON inválido, body vacío, HTTP 500, body excesivo, y no-filtración de
    // marcadores sintéticos sensibles.
    // ══════════════════════════════════════════════════════════════════════

    // §11 — HTTP 400 con código y mensaje seguros, MÁS datos sintéticos
    // deliberadamente sensibles en campos NO permitidos (fuera de la
    // allowlist) — deben quedar completamente descartados.
    [Fact]
    public async Task Http400_WithSafeErrorFields_ExposesStructuredDiagnostics_NeverLeaksNonAllowlistedFields()
    {
        const string sensitiveAccountId = "SYNTHETIC-ACCOUNT-ID-MARKER-0000001";
        const string sensitiveToken     = "SYNTHETIC-TOKEN-MARKER-0000002";
        const string sensitiveEmail     = "synthetic@example.invalid";

        var body = $$"""
            {
              "code": "INVALID_CHANNEL",
              "message": "The channel value is not valid for this key type.",
              "account_id": "{{sensitiveAccountId}}",
              "authorization": "Bearer {{sensitiveToken}}",
              "owner": { "email": "{{sensitiveEmail}}" }
            }
            """;
        var logger  = new CapturingLogger<PassportHttpClient>();
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, body));
        var client  = CreateClient(handler, ValidConfig(), logger: logger);

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("INVALID_CHANNEL", ex.SafeErrorCode);
        Assert.Equal("The channel value is not valid for this key type.", ex.SafeErrorMessage);

        AssertNoSensitiveLeak(ex, logger, sensitiveAccountId, sensitiveToken, sensitiveEmail);
    }

    // §19 — el propio campo "message" (allowlisted) contiene un patrón
    // sensible embebido en texto libre: debe descartarse POR COMPLETO, no
    // "limpiarse". Theory cubriendo las categorías mínimas exigidas del
    // ticket (§8/§19) — usa los patrones REALES del denylist
    // (PassportErrorBodySanitizer.SensitivePatterns), en snake_case, que es
    // la convención de nombres real de Passport (mismo criterio ya usado en
    // toda esta integración: key_id, key_value, customer_id, account_id,
    // qr_code_data, qr_code_image).
    [Theory]
    [InlineData("key_id")]
    [InlineData("key_value")]
    [InlineData("customer_id")]
    [InlineData("account_id")]
    [InlineData("client_secret")]
    [InlineData("api_key")]
    [InlineData("api_secret")]
    [InlineData("access_token")]
    [InlineData("authorization")]
    [InlineData("bearer")]
    [InlineData("identification_number")]
    [InlineData("qr_code_data")]
    [InlineData("qr_code_image")]
    [InlineData("9999999999")] // secuencia larga de dígitos (identification_number/phone-like).
    [InlineData("synthetic@example.invalid")]
    public async Task Http400_MessageContainingSensitiveMarker_IsDiscardedEntirely(string sensitiveMarker)
    {
        var body = $$"""{ "code": "REJECTED", "message": "rejected value: {{sensitiveMarker}}" }""";
        var logger  = new CapturingLogger<PassportHttpClient>();
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, body));
        var client  = CreateClient(handler, ValidConfig(), logger: logger);

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal("REJECTED", ex.SafeErrorCode);   // code no contenía el marcador — se conserva.
        Assert.Null(ex.SafeErrorMessage);             // message SÍ lo contenía — descartado por completo.

        AssertNoSensitiveLeak(ex, logger, sensitiveMarker);
    }

    // §12 — JSON inválido: status code disponible, sin body, sin excepción
    // secundaria del sanitizador/parser.
    [Fact]
    public async Task Http400_MalformedJsonBody_FallsBackToGenericDiagnosticsWithoutCrash()
    {
        const string malformedBody = "{ this is not valid json ";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, malformedBody));
        var client  = CreateClient(handler, ValidConfig());

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Null(ex.SafeErrorCode);
        Assert.Null(ex.SafeErrorMessage);
    }

    // §13 — body vacío: status code disponible, ErrorCode/SafeMessage null,
    // sin excepción secundaria.
    [Fact]
    public async Task Http400_EmptyBody_StatusCodeAvailable_NoSecondaryException()
    {
        var handler = new FakeHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client  = CreateClient(handler, ValidConfig());

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Null(ex.SafeErrorCode);
        Assert.Null(ex.SafeErrorMessage);
    }

    // §14 — HTTP 500: mismo mecanismo seguro, no asumido exclusivo de 400.
    [Fact]
    public async Task Http500_UsesSameSafeDiagnosticMechanism()
    {
        const string body = """{ "code": "INTERNAL_ERROR", "message": "Unexpected server error." }""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, body));
        var client  = CreateClient(handler, ValidConfig());

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("INTERNAL_ERROR", ex.SafeErrorCode);
        Assert.Equal("Unexpected server error.", ex.SafeErrorMessage);
    }

    // §15 — body que excede el límite de diagnóstico: se descarta POR
    // COMPLETO (nunca truncado-y-expuesto); status code sigue disponible.
    [Fact]
    public async Task Http400_OversizedBody_DiscardsBodyEntirely_StatusCodeStillAvailable()
    {
        var oversizedMessage = new string('A', PassportErrorBodySanitizer.MaxBodyLengthForDiagnosticsBytes + 100);
        var body = $$"""{ "code": "TOO_BIG", "message": "{{oversizedMessage}}" }""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, body));
        var client  = CreateClient(handler, ValidConfig());

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Null(ex.SafeErrorCode);
        Assert.Null(ex.SafeErrorMessage);
    }

    // Regresión: campo extraído más largo que el límite por-campo (pero el
    // body completo sigue bajo el límite global) se trunca, no se descarta.
    [Fact]
    public async Task Http400_FieldLongerThanPerFieldLimit_IsTruncatedNotDiscarded()
    {
        var longButSafeMessage = new string('B', PassportErrorBodySanitizer.MaxExtractedFieldLength + 50);
        var body = $$"""{ "code": "LONG_MESSAGE", "message": "{{longButSafeMessage}}" }""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, body));
        var client  = CreateClient(handler, ValidConfig());

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.NotNull(ex.SafeErrorMessage);
        Assert.Equal(PassportErrorBodySanitizer.MaxExtractedFieldLength, ex.SafeErrorMessage!.Length);
    }

    // Regresión: campos no-string (objeto/número) en las posiciones
    // allowlisted se ignoran — nunca se serializa un objeto/array como si
    // fuera texto.
    [Fact]
    public async Task Http400_NonStringAllowlistedField_IsIgnored()
    {
        const string body = """{ "code": 12345, "message": { "nested": "object" } }""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, body));
        var client  = CreateClient(handler, ValidConfig());

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.PostAsync<SyntheticRequest, SyntheticResponse>("/v1/x", new SyntheticRequest("x")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Null(ex.SafeErrorCode);
        Assert.Null(ex.SafeErrorMessage);
    }

    // ── XPAY-293 — DELETE genérico ───────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SendsDeleteWithBearerToCorrectPath()
    {
        var tokenProvider = new FakePassportTokenProvider("obtained-token");
        var handler = new FakeHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler, ValidConfig(), tokenProvider);

        await client.DeleteAsync("/v1/some-future-endpoint/123");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("/v1/some-future-endpoint/123", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequestBody);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task DeleteAsync_204NoContent_CompletesWithoutAttemptingDeserialization()
    {
        // Respuesta 204 SIN ningún HttpContent asignado (a diferencia de
        // FakeHttpMessageHandler.Json, que siempre asigna StringContent) —
        // el escenario real más fiel a "sin body en absoluto". Si DeleteAsync
        // intentara deserializar, esto fallaría con una excepción de
        // protocolo; el hecho de que complete sin lanzar prueba que no lo hace.
        var handler = new FakeHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler, ValidConfig());

        await client.DeleteAsync("/v1/x");

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task DeleteAsync_NonSuccessStatus_FollowsExistingErrorPattern()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, "{\"error\":\"forbidden\"}"));
        var client = CreateClient(handler, ValidConfig());

        await Assert.ThrowsAsync<PassportAuthenticationException>(() => client.DeleteAsync("/v1/x"));
    }
}
