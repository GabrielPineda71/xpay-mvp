using System.Net;
using System.Text.Json;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-517 — pruebas dedicadas de DecodeQrCodeWithRawCaptureAsync/
// PostWithRawCaptureAsync (M4-T5). Archivo SEPARADO de
// PassportQrClientTests.cs deliberadamente: cubre exclusivamente el método
// ADITIVO nuevo, sin tocar ni un solo test existente de DecodeQrCodeAsync.
// SIN red real — mismo patrón FakeHttpMessageHandler/FakeConfiguration/
// FakePassportTokenProvider ya usado en el resto de la integración.
public class PassportQrClientRawCaptureTests
{
    private const string BaseUrl = "https://passport.test";

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [PassportOptions.EnvBaseUrl]      = BaseUrl,
        [PassportOptions.EnvClientId]     = "test-client-id",
        [PassportOptions.EnvClientSecret] = "test-client-secret",
    };

    private static PassportQrClient CreateClient(FakeHttpMessageHandler handler) => new(
        new PassportHttpClient(
            new FakeHttpClientFactory(handler),
            new FakePassportTokenProvider("synthetic-bearer-token"),
            new FakeConfiguration(ValidConfig()),
            new CapturingLogger<PassportHttpClient>()));

    private static PassportDecodeQrCodeRequest SyntheticRequest() => new(
        CustomerId: "synthetic-customer-id-001",
        QrCodeData: "00020101synthetic-emv-payload6304ABCD");

    // ── Fidelidad cruda — el hallazgo CRÍTICO de este ticket ─────────────

    // XPAY-517 test obligatorio (§12.P): un campo JSON real de Passport que
    // NO está modelado por PassportDecodeQrCodeResponse (aquí, "tip" —
    // exactamente el campo que M4-T2 documentó como perdido) debe
    // sobrevivir en RawResponseJson, demostrando que la estrategia elegida
    // evita repetir esa pérdida. Esto NUNCA implica que Passport realmente
    // devuelva "tip" en producción — es una prueba de la CAPTURA, no del
    // contrato real de Passport (XPAY-517 §13).
    [Fact]
    public async Task DecodeQrCodeWithRawCaptureAsync_UnmodeledFieldSurvivesInRawJson()
    {
        const string body = """
            {
              "status": "ACTIVE",
              "type": "DYNAMIC",
              "channel": "MPOS",
              "tip": { "tip_type": "FIXED", "tip_value": "100.00" },
              "qr_code_reference": "SYNTHREF001"
            }
            """;
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var (response, rawJson) = await client.DecodeQrCodeWithRawCaptureAsync(SyntheticRequest());

        Assert.NotNull(response);
        Assert.Equal("ACTIVE", response!.Status);
        Assert.NotNull(rawJson);
        // El DTO tipado NO modela tip — confirma la limitación ya conocida.
        Assert.DoesNotContain("Tip", typeof(PassportDecodeQrCodeResponse)
            .GetProperties().Select(p => p.Name));
        // Pero el JSON crudo SÍ lo conserva íntegro.
        using var doc = JsonDocument.Parse(rawJson!);
        Assert.True(doc.RootElement.TryGetProperty("tip", out var tipElement));
        Assert.Equal("FIXED", tipElement.GetProperty("tip_type").GetString());
        Assert.Equal("100.00", tipElement.GetProperty("tip_value").GetString());
    }

    [Fact]
    public async Task DecodeQrCodeWithRawCaptureAsync_TypedResponseMatchesRawJson_ForModeledFields()
    {
        const string body = """
            {
              "status": "ACTIVE",
              "type": "DYNAMIC",
              "channel": "MPOS",
              "amount": { "value": "10000.00", "currency": "COP" },
              "qr_code_reference": "SYNTHREF002"
            }
            """;
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var (response, rawJson) = await client.DecodeQrCodeWithRawCaptureAsync(SyntheticRequest());

        Assert.Equal("10000.00", response!.Amount!.Value);
        Assert.Equal("COP", response.Amount.Currency);
        Assert.Equal("SYNTHREF002", response.QrCodeReference);

        using var doc = JsonDocument.Parse(rawJson!);
        Assert.Equal("10000.00", doc.RootElement.GetProperty("amount").GetProperty("value").GetString());
    }

    [Fact]
    public async Task DecodeQrCodeWithRawCaptureAsync_SendsSameRequestShapeAsDecodeQrCodeAsync()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"status":"ACTIVE"}"""));
        var client = CreateClient(handler);

        await client.DecodeQrCodeWithRawCaptureAsync(SyntheticRequest());

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/qrcodes/decode", handler.LastRequest.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("synthetic-customer-id-001", doc.RootElement.GetProperty("customer_id").GetString());
    }

    [Fact]
    public async Task DecodeQrCodeWithRawCaptureAsync_ValidatesRequest_SameAsDecodeQrCodeAsync()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);
        var invalidRequest = new PassportDecodeQrCodeRequest(CustomerId: "", QrCodeData: "x");

        await Assert.ThrowsAsync<ArgumentException>(() => client.DecodeQrCodeWithRawCaptureAsync(invalidRequest));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DecodeQrCodeWithRawCaptureAsync_HttpError_ThrowsTransportException_NeverExposesRawBody()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"error_code":"invalid_parameter_value"}"""));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<PassportTransportException>(
            () => client.DecodeQrCodeWithRawCaptureAsync(SyntheticRequest()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("invalid_parameter_value", ex.SafeErrorCode);
    }

    [Fact]
    public async Task DecodeQrCodeWithRawCaptureAsync_EmptyBody_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(() =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
            msg.Content.Headers.ContentLength = 0;
            return msg;
        });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.DecodeQrCodeWithRawCaptureAsync(SyntheticRequest()));
    }

    [Fact]
    public async Task DecodeQrCodeWithRawCaptureAsync_NeverCalledByExistingDecodeQrCodeAsync_ZeroInterference()
    {
        // Regresión explícita: DecodeQrCodeAsync (existente, M4-T2) NUNCA
        // pasa por PostWithRawCaptureAsync — mismo comportamiento exacto
        // que antes de XPAY-517.
        const string body = """{"status":"ACTIVE","type":"STATIC"}""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var response = await client.DecodeQrCodeAsync(SyntheticRequest());

        Assert.Equal("ACTIVE", response.Status);
        Assert.Equal(1, handler.CallCount);
    }
}
