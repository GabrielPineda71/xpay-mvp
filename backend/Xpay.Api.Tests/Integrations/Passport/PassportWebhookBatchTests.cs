using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-602 — soporte de entregas en LOTE (raíz JSON Array).
//
// Motivo real: en XPAY-601, Passport Sandbox entregó 36 webhooks con firma
// HMAC-SHA256 V2 VÁLIDA (36/36, enforcing=True, cero 401) que el parser
// rechazó con 400 porque exigía una raíz de tipo objeto. El canonicalizador
// había parseado esos mismos cuerpos sin problema, de modo que el JSON era
// válido y la única causa posible era la forma de la raíz.
//
// Todos los datos de este archivo son SINTÉTICOS. No se copió ningún payload
// real: sólo se reprodujeron sus características estructurales. Ningún
// identificador, nombre, cuenta ni secreto real aparece aquí.
public class PassportWebhookBatchTests
{
    private const string Pid = "xpay602-11111111-2222-3333";

    // Lote sintético con las tres transiciones entrantes del MISMO payment_id.
    // Deliberadamente con saltos de línea y espacios: la forma canónica —y por
    // tanto la firma— no depende del formato de transmisión.
    private const string BatchRaw = """
        [
          { "Event": "payment.inbound.received", "payment_id": "xpay602-11111111-2222-3333",
            "status": "PROCESSING",
            "amount": { "value": "1.00", "currency": "COP" },
            "destination_key": { "key_type": "BCODE", "key_value": "SINTETICO-XPAY" },
            "qr_code_reference": "QR-SINT-602",
            "created_at": "2026-09-30T21:25:10.000000Z", "updated_at": "2026-09-30T21:25:10.000000Z" },
          { "Event": "payment.inbound.confirmed", "payment_id": "xpay602-11111111-2222-3333",
            "status": "CONFIRMED",
            "created_at": "2026-09-30T21:25:11.000000Z", "updated_at": "2026-09-30T21:25:11.000000Z" },
          { "Event": "payment.inbound.settled", "payment_id": "xpay602-11111111-2222-3333",
            "status": "SETTLED", "settled_at": "2026-09-30T21:25:12.000000Z",
            "created_at": "2026-09-30T21:25:10.000000Z", "updated_at": "2026-09-30T21:25:12.000000Z" }
        ]
        """;

    private const string SingleRaw =
        "{\"Event\":\"payment.inbound.received\",\"payment_id\":\"" + Pid + "\",\"status\":\"PROCESSING\"}";

    // --- Caso 1 — raíz objeto: comportamiento existente intacto ------------

    [Fact]
    public void Caso01_RaizObjeto_SigueSiendoEventoUnico()
    {
        var shape = PassportWebhookPayloadParser.TryParseDelivery(SingleRaw, out var p);

        Assert.Equal(PassportWebhookDeliveryShape.SingleEvent, shape);
        Assert.Single(p);
        Assert.Equal(PassportInboundEventKind.Received, p[0].Kind);
        Assert.True(p[0].IsProcessableInbound);

        // La API anterior sigue funcionando igual — sin regresión.
        Assert.True(PassportWebhookPayloadParser.TryParse(SingleRaw, out var uno));
        Assert.Equal(Pid, uno.PaymentId);
    }

    // --- Casos 2 y 3 — raíz array -----------------------------------------

    [Fact]
    public void Caso02_ArrayConUnEventoEntrante()
    {
        var shape = PassportWebhookPayloadParser.TryParseDelivery("[" + SingleRaw + "]", out var p);

        Assert.Equal(PassportWebhookDeliveryShape.Batch, shape);
        Assert.Single(p);
        Assert.Equal(PassportInboundEventKind.Received, p[0].Kind);
    }

    [Fact]
    public void Caso03_ArrayConVariosEventosEntrantes_TodosProyectados()
    {
        var shape = PassportWebhookPayloadParser.TryParseDelivery(BatchRaw, out var p);

        Assert.Equal(PassportWebhookDeliveryShape.Batch, shape);
        Assert.Equal(3, p.Count);
        Assert.All(p, x => Assert.True(x.IsProcessableInbound));
    }

    // --- Caso 14 — mismo payment_id, tres estados: NO son duplicados -------

    [Fact]
    public void Caso14_TresEstadosDelMismoPago_SonEventosDistintos()
    {
        PassportWebhookPayloadParser.TryParseDelivery(BatchRaw, out var p);

        Assert.All(p, x => Assert.Equal(Pid, x.PaymentId));
        Assert.Equal(
            new[] { PassportInboundEventKind.Received, PassportInboundEventKind.Confirmed, PassportInboundEventKind.Settled },
            p.Select(x => x.Kind).ToArray());

        // La clave de idempotencia es (event_type, payment_id): al compartir
        // payment_id, lo que los distingue es el nombre de evento. Si fuese
        // sólo payment_id, estas tres entregas legítimas se perderían.
        Assert.Equal(3, p.Select(x => (x.EventName, x.PaymentId)).Distinct().Count());
        Assert.Single(p.Select(x => x.PaymentId).Distinct());
    }

    // --- Caso 6 — salientes en el lote: clasificados, sin efecto financiero -

    [Fact]
    public void Caso06_SalientesEnElLote_SeClasificanNotInbound()
    {
        const string mixto =
            "[{\"Event\":\"payment.inbound.received\",\"payment_id\":\"a\"}," +
            " {\"Event\":\"payment.outbound.settled\",\"payment_id\":\"a\"}," +
            " {\"Event\":\"payment.outbound.confirmed\",\"payment_id\":\"a\"}]";

        var shape = PassportWebhookPayloadParser.TryParseDelivery(mixto, out var p);

        Assert.Equal(PassportWebhookDeliveryShape.Batch, shape);
        Assert.Equal(3, p.Count);
        Assert.Equal(PassportInboundEventKind.Received,  p[0].Kind);
        Assert.Equal(PassportInboundEventKind.NotInbound, p[1].Kind);
        Assert.Equal(PassportInboundEventKind.NotInbound, p[2].Kind);
        Assert.False(p[1].IsProcessableInbound);
        Assert.False(p[2].IsProcessableInbound);
    }

    // --- Caso 7 — array vacío: comportamiento explícito --------------------

    [Fact]
    public void Caso07_ArrayVacio_EsFormaPropia_NoInvalido()
    {
        var shape = PassportWebhookPayloadParser.TryParseDelivery("[]", out var p);

        // Estructuralmente válido pero sin eventos. El controller responde 200:
        // no hay nada que procesar y reintentar sería inútil.
        Assert.Equal(PassportWebhookDeliveryShape.EmptyBatch, shape);
        Assert.Empty(p);
    }

    // --- Casos 8 y 9 — fail-closed ante error estructural ------------------

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("[\"cadena\"]")]
    [InlineData("[null]")]
    [InlineData("[[{\"Event\":\"payment.inbound.received\"}]]")]
    [InlineData("[true]")]
    public void Caso08_ElementoNoObjeto_EsInvalido(string raw)
    {
        var shape = PassportWebhookPayloadParser.TryParseDelivery(raw, out var p);

        Assert.Equal(PassportWebhookDeliveryShape.Invalid, shape);
        Assert.Empty(p);
    }

    [Fact]
    public void Caso09_UnElementoInvalidoEntreValidos_InvalidaTodoElLote_SinProyeccionParcial()
    {
        const string raw =
            "[{\"Event\":\"payment.inbound.received\",\"payment_id\":\"a\"}," +
            " 42," +
            " {\"Event\":\"payment.inbound.settled\",\"payment_id\":\"a\"}]";

        var shape = PassportWebhookPayloadParser.TryParseDelivery(raw, out var p);

        Assert.Equal(PassportWebhookDeliveryShape.Invalid, shape);
        // Lo esencial: NINGÚN elemento se proyecta. La validación estructural
        // del lote completo precede a cualquier proyección, de modo que el
        // servicio nunca puede persistir un lote a medias.
        Assert.Empty(p);
    }

    [Theory]
    [InlineData("no-es-json")]
    [InlineData("\"solo-una-cadena\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("")]
    public void RaicesNoObjetoNiArray_SonInvalidas(string raw)
        => Assert.Equal(
            PassportWebhookDeliveryShape.Invalid,
            PassportWebhookPayloadParser.TryParseDelivery(raw, out _));

    // --- Casos 10, 11 y 12 — firma sobre el CUERPO COMPLETO que es array ---
    //
    // XPAY_INTERNAL_TEST_VECTOR: generado con la implementación de REFERENCIA
    // del contrato (json.dumps de Python + HMAC-SHA256) sobre el lote sintético
    // de arriba. Passport no publica ningún vector.

    private const string Secret    = "xpay-internal-test-secret";
    private const string Timestamp = "1790000000";
    private const string BatchHmacHex =
        "ea1909ff9f841f484aafded2f6dc6b258ba74e981c43b67155da65f5743b9a27";

    private static readonly DateTimeOffset AtTimestamp =
        DateTimeOffset.FromUnixTimeSeconds(1790000000);

    private static PassportWebhookSignatureValidator Validator(
        string? mode = null, DateTimeOffset? now = null)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
               {
                   [PassportWebhookOptions.EnvSecret] = Secret,
                   [PassportWebhookOptions.EnvSignatureMode] = mode,
               }).Build(),
               new TestTimeProvider(now ?? AtTimestamp),
               NullLogger<PassportWebhookSignatureValidator>.Instance);

    [Fact]
    public void Caso10_FirmaValidaSobreUnCuerpoArray_EsAceptada()
    {
        var r = Validator().Validate(BatchRaw, BatchHmacHex, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.Valid, r.Outcome);
        Assert.True(r.SignatureValid);
        Assert.False(r.ShouldReject);
    }

    [Fact]
    public void Caso10b_LaFirmaDelArrayEsIndiferenteAlFormato()
    {
        // Mismo lote re-serializado en forma canónica: la firma sigue valiendo.
        var canonico = PassportWebhookCanonicalJson.Canonicalize(BatchRaw);
        Assert.StartsWith("[", canonico);
        Assert.True(Validator().Validate(canonico, BatchHmacHex, Timestamp).SignatureValid);
    }

    [Fact]
    public void Caso11_FirmaInvalidaSobreArray_Rechaza401EnEnforce()
    {
        var r = Validator().Validate(BatchRaw, BatchHmacHex.Replace('e', 'f'), Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.SignatureInvalid, r.Outcome);
        Assert.False(r.SignatureValid);
        Assert.True(r.ShouldReject);
    }

    [Fact]
    public void Caso11b_AlterarUnElementoDelLote_InvalidaLaFirmaDelTodo()
    {
        var alterado = BatchRaw.Replace("CONFIRMED", "SETTLED");
        Assert.False(Validator().Validate(alterado, BatchHmacHex, Timestamp).SignatureValid);
    }

    [Fact]
    public void Caso12_TimestampFueraDeVentana_RechazaTambienEnLotes()
    {
        var tarde = AtTimestamp.AddSeconds(PassportWebhookOptions.DefaultTimestampToleranceSeconds + 1);
        var r = Validator(now: tarde).Validate(BatchRaw, BatchHmacHex, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.TimestampOutOfWindow, r.Outcome);
        Assert.True(r.ShouldReject);
    }

    [Fact]
    public void Caso12b_FirmaAusenteSobreArray_RechazaEnEnforce_YObservaEnSandbox()
    {
        Assert.True(Validator().Validate(BatchRaw, null, Timestamp).ShouldReject);

        var observe = Validator(mode: PassportWebhookOptions.ModeObserve)
            .Validate(BatchRaw, null, null);
        Assert.False(observe.ShouldReject);
        Assert.False(observe.SignatureValid);
    }

    // --- Sanitización por elemento del lote --------------------------------

    [Fact]
    public void CadaElementoDelLoteSeSanitizaIndividualmente()
    {
        PassportWebhookPayloadParser.TryParseDelivery(BatchRaw, out var p);

        foreach (var ev in p)
        {
            var s = PassportWebhookPayloadParser.BuildSanitizedJson(ev);
            Assert.DoesNotContain("sender", s);
            Assert.DoesNotContain("receiver", s);
            Assert.DoesNotContain("key_value", s);
            Assert.DoesNotContain("SINTETICO-XPAY", s);   // el valor de la llave nunca se persiste
        }

        // El tipo de llave sí se conserva donde venía.
        var recibido = p.First(x => x.Kind == PassportInboundEventKind.Received);
        Assert.Equal("BCODE", recibido.DestinationKeyType);
        Assert.True(recibido.DestinationKeyPresent);
    }
}
