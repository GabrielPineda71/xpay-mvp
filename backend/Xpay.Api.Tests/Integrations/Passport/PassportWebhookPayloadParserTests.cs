using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-594 — parseo y SANITIZACIÓN del payload. Los datos de estos payloads
// son sintéticos, calcados de la forma de los ejemplos públicos de Passport.
public class PassportWebhookPayloadParserTests
{
    private const string ReceivedRaw =
        "{\"type\":\"BREB\"," +
        "\"amount\":{\"value\":\"1.00\",\"currency\":\"COP\"}," +
        "\"sender\":{\"owner\":{\"name\":\"NOMBRE SINTETICO\",\"identification_type\":\"CC\"," +
        "\"identification_number\":\"000000000\"},\"account\":{\"account_type\":\"ORDINARY\"," +
        "\"account_number\":\"000000001\"}}," +
        "\"receiver\":{\"owner\":{\"name\":\"XPAY SINTETICO\"}," +
        "\"account\":{\"account_number\":\"000000002\"}," +
        "\"key\":{\"key_type\":\"EMAIL\",\"key_value\":\"sintetico@example.test\"}}," +
        "\"status\":\"PROCESSING\"," +
        "\"payment_id\":\"11111111-2222-3333-4444-555555555555\"," +
        "\"account_id\":\"66666666-7777-8888-9999-000000000000\"," +
        "\"destination_key\":{\"key_type\":\"EMAIL\",\"key_value\":\"sintetico@example.test\"}," +
        "\"qr_code_reference\":\"QR-SINTETICO-1\"," +
        "\"end_to_end_identification\":\"SINTETICO-E2E-1\"," +
        "\"created_at\":\"2026-03-10T17:44:52.111820Z\"," +
        "\"updated_at\":\"2026-03-10T17:44:52.111820Z\"," +
        "\"Event\":\"payment.inbound.received\"}";

    [Fact]
    public void ParseaLosCamposNecesariosParaT7()
    {
        Assert.True(PassportWebhookPayloadParser.TryParse(ReceivedRaw, out var p));

        Assert.Equal("payment.inbound.received", p.EventName);
        Assert.Equal(PassportInboundEventKind.Received, p.Kind);
        Assert.Equal("11111111-2222-3333-4444-555555555555", p.PaymentId);
        Assert.Equal("PROCESSING", p.Status);
        Assert.Equal("66666666-7777-8888-9999-000000000000", p.AccountId);
        Assert.Equal("EMAIL", p.DestinationKeyType);
        Assert.True(p.DestinationKeyPresent);
        Assert.Equal("QR-SINTETICO-1", p.QrCodeReference);
        Assert.Equal("SINTETICO-E2E-1", p.EndToEndIdentification);
        Assert.Equal("1.00", p.AmountValue);
        Assert.Equal("COP", p.AmountCurrency);
        Assert.True(p.IsProcessableInbound);
    }

    // Validación de T7: un evento entrante reconocido CON payment_id.
    [Fact]
    public void EventoEntranteSinPaymentId_NoEsProcesable()
    {
        Assert.True(PassportWebhookPayloadParser.TryParse(
            "{\"Event\":\"payment.inbound.settled\"}", out var p));

        Assert.Equal(PassportInboundEventKind.Settled, p.Kind);
        Assert.False(p.HasPaymentId);
        Assert.False(p.IsProcessableInbound);
    }

    [Fact]
    public void EventoSaliente_NoEsProcesableComoEntrante()
    {
        Assert.True(PassportWebhookPayloadParser.TryParse(
            "{\"Event\":\"payment.outbound.settled\",\"payment_id\":\"x\"}", out var p));

        Assert.Equal(PassportInboundEventKind.NotInbound, p.Kind);
        Assert.False(p.IsProcessableInbound);
    }

    [Fact]
    public void Rechazo_ProyectaElCodigoYLaDescripcionDeError()
    {
        const string raw =
            "{\"error\":{\"error_code\":\"B101\",\"error_description\":\"Payment rejected\"}," +
            "\"payment_id\":\"abc\",\"Event\":\"payment.inbound.rejected\"}";

        Assert.True(PassportWebhookPayloadParser.TryParse(raw, out var p));

        Assert.Equal(PassportInboundEventKind.Rejected, p.Kind);
        Assert.Equal("B101", p.ErrorCode);
        Assert.Equal("Payment rejected", p.ErrorDescription);
        Assert.True(p.IsProcessableInbound);
    }

    [Theory]
    [InlineData("no-es-json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"solo-una-cadena\"")]
    [InlineData("")]
    public void CuerposNoValidos_NoParsean(string raw)
        => Assert.False(PassportWebhookPayloadParser.TryParse(raw, out _));

    // --- SANITIZACIÓN: la garantía central de privacidad ---------------------

    [Fact]
    public void JsonSanitizado_NoContienePiiNiDatosBancarios()
    {
        Assert.True(PassportWebhookPayloadParser.TryParse(ReceivedRaw, out var p));
        var sanitized = PassportWebhookPayloadParser.BuildSanitizedJson(p);

        // Bloques completos de PII y datos bancarios omitidos.
        Assert.DoesNotContain("sender", sanitized);
        Assert.DoesNotContain("receiver", sanitized);
        Assert.DoesNotContain("NOMBRE SINTETICO", sanitized);
        Assert.DoesNotContain("XPAY SINTETICO", sanitized);
        Assert.DoesNotContain("identification_number", sanitized);
        Assert.DoesNotContain("account_number", sanitized);
        Assert.DoesNotContain("000000001", sanitized);
        Assert.DoesNotContain("000000002", sanitized);

        // El VALOR de la llave destino tampoco se persiste: una llave Bre-B
        // puede ser un teléfono, un correo o un documento.
        Assert.DoesNotContain("sintetico@example.test", sanitized);
        Assert.DoesNotContain("key_value", sanitized);

        // Lo que sí queda: tipo de llave, presencia, y los campos de T7.
        Assert.Contains("\"destination_key_type\":\"EMAIL\"", sanitized);
        Assert.Contains("\"destination_key_present\":true", sanitized);
        Assert.Contains("\"payment_id\":\"11111111-2222-3333-4444-555555555555\"", sanitized);
        Assert.Contains("\"status\":\"PROCESSING\"", sanitized);
        Assert.Contains("campos_omitidos_por_privacidad", sanitized);
    }

    [Fact]
    public void HashDelPayload_EsSha256HexEstableYDistintoPorCuerpo()
    {
        var h1 = PassportWebhookPayloadParser.ComputePayloadHash(ReceivedRaw);
        var h2 = PassportWebhookPayloadParser.ComputePayloadHash(ReceivedRaw);
        var h3 = PassportWebhookPayloadParser.ComputePayloadHash(
            ReceivedRaw.Replace("PROCESSING", "CONFIRMED"));

        Assert.Equal(64, h1.Length);
        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);
        Assert.Equal(h1, h1.ToLowerInvariant());
    }
}
