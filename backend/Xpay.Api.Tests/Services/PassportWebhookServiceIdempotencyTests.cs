using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-594 — M5-T6/T7: idempotencia de la recepción del webhook entrante.
//
// Mismo patrón SQL que el resto del repositorio (ver
// CarteraOrdinariaEstadosActivosTests): estas pruebas requieren una base de
// datos real, se SALTAN en local si no hay cadena de conexión y son
// OBLIGATORIAS en CI. No existe proveedor en memoria en este proyecto y no se
// introduce uno aquí.
//
// La tabla passport_webhook_events se toca en aislamiento: un pago entrante no
// tiene retiro asociado, así que no hay dependencias con Wallet, Ledger ni
// breb_retiros y cada prueba limpia sus propias filas.
[Collection("SqlIntegration")]
public sealed class PassportWebhookServiceIdempotencyTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 17, 44, 52, TimeSpan.Zero);

    private static string RawInbound(string paymentId, string evento, string status) =>
        "{\"Event\":\"" + evento + "\"," +
        "\"payment_id\":\"" + paymentId + "\"," +
        "\"status\":\"" + status + "\"," +
        "\"destination_key\":{\"key_type\":\"EMAIL\",\"key_value\":\"sintetico@example.test\"}," +
        "\"qr_code_reference\":\"QR-SINTETICO\"," +
        "\"created_at\":\"2026-03-10T17:44:52.111820Z\"," +
        "\"updated_at\":\"2026-03-10T17:44:52.111820Z\"}";

    private static PassportWebhookService Servicio(XpayDbContext ctx)
        => new(
            ctx,
            new FijoTimeProvider(T0),
            new LibranzaRecaudoAplicacionService(
                ctx,
                new ConfigurationBuilder().Build(),
                NullLogger<LibranzaRecaudoAplicacionService>.Instance),
            NullLogger<PassportWebhookService>.Instance);

    private static async Task<PassportWebhookProcessOutcome> ProcesarAsync(
        XpayDbContext ctx, string raw)
    {
        Assert.True(PassportWebhookPayloadParser.TryParse(raw, out var payload));
        return await Servicio(ctx).ProcessAsync(raw, payload, signatureValid: true);
    }

    // 1 — primera entrega: se registra y queda procesada.
    [Fact]
    public async Task PrimeraEntrega_SeRegistraYQuedaProcesada()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);
            var raw = RawInbound(pid, PassportInboundEventClassifier.EventInboundReceived, "PROCESSING");

            var r = await ProcesarAsync(ctx, raw);

            Assert.Equal(PassportWebhookProcessResult.Processed, r.Result);
            Assert.Equal(PassportInboundEventKind.Received, r.Kind);

            var filas = await Filas(ctx, pid);
            var fila = Assert.Single(filas);
            Assert.True(fila.Processed);
            Assert.True(fila.SignatureValid);
            Assert.NotNull(fila.FechaProcesado);
            Assert.Null(fila.IdBrebRetiro);
            Assert.Equal(PassportInboundEventClassifier.EventInboundReceived, fila.EventType);

            // Persistencia sanitizada: ni el valor de la llave ni PII.
            Assert.DoesNotContain("sintetico@example.test", fila.PayloadSanitizedJson);
            Assert.DoesNotContain("key_value", fila.PayloadSanitizedJson);
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // 2 — reintento IDÉNTICO: no se reprocesa ni se duplica la fila.
    [Fact]
    public async Task ReintentoIdentico_DevuelveDuplicadoYNoInsertaOtraFila()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);
            var raw = RawInbound(pid, PassportInboundEventClassifier.EventInboundReceived, "PROCESSING");

            var primera = await ProcesarAsync(ctx, raw);
            var reintento = await ProcesarAsync(ctx, raw);

            Assert.Equal(PassportWebhookProcessResult.Processed, primera.Result);
            Assert.Equal(PassportWebhookProcessResult.Duplicate, reintento.Result);
            Assert.Equal(primera.IdEvent, reintento.IdEvent);
            Assert.Single(await Filas(ctx, pid));
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // 3 — mismo payment_id con evento DISTINTO: son entregas legítimamente
    // diferentes y ambas deben procesarse. Es la razón por la que payment_id
    // por sí solo no puede ser la clave de idempotencia.
    [Fact]
    public async Task MismoPaymentId_ConEventoDistinto_SeProcesaComoEntregaNueva()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);

            var received = await ProcesarAsync(ctx,
                RawInbound(pid, PassportInboundEventClassifier.EventInboundReceived, "PROCESSING"));
            var confirmed = await ProcesarAsync(ctx,
                RawInbound(pid, PassportInboundEventClassifier.EventInboundConfirmed, "CONFIRMED"));
            var settled = await ProcesarAsync(ctx,
                RawInbound(pid, PassportInboundEventClassifier.EventInboundSettled, "SETTLED"));

            Assert.Equal(PassportWebhookProcessResult.Processed, received.Result);
            Assert.Equal(PassportWebhookProcessResult.Processed, confirmed.Result);
            Assert.Equal(PassportWebhookProcessResult.Processed, settled.Result);

            // CONFIRMED se clasifica correctamente — lo que BrebPaymentStateMachine
            // NO sabría hacer.
            Assert.Equal(PassportInboundEventKind.Confirmed, confirmed.Kind);
            Assert.Equal(3, (await Filas(ctx, pid)).Count);
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // 4 — payload_hash DISTINTO para el mismo (evento, pago): sigue siendo
    // duplicado. El hash es evidencia secundaria, nunca identidad primaria.
    [Fact]
    public async Task HashDistintoParaElMismoEventoYPago_SigueSiendoDuplicado()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);
            var raw1 = RawInbound(pid, PassportInboundEventClassifier.EventInboundReceived, "PROCESSING");
            // Reintento con updated_at distinto: otro cuerpo, otro hash, misma entrega.
            var raw2 = raw1.Replace("17:44:52.111820Z\"}", "17:44:53.222000Z\"}");

            Assert.NotEqual(
                PassportWebhookPayloadParser.ComputePayloadHash(raw1),
                PassportWebhookPayloadParser.ComputePayloadHash(raw2));

            var primera = await ProcesarAsync(ctx, raw1);
            var segunda = await ProcesarAsync(ctx, raw2);

            Assert.Equal(PassportWebhookProcessResult.Processed, primera.Result);
            Assert.Equal(PassportWebhookProcessResult.Duplicate, segunda.Result);
            Assert.Single(await Filas(ctx, pid));

            // El hash de la primera entrega se conserva: no se sobreescribe con
            // el del reintento.
            var fila = Assert.Single(await Filas(ctx, pid));
            Assert.Equal(PassportWebhookPayloadParser.ComputePayloadHash(raw1), fila.PayloadHash);
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // 5 — evento SALIENTE: se registra para auditoría pero NO se procesa aquí.
    [Fact]
    public async Task EventoSaliente_SeRegistraSinProcesar()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);

            var r = await ProcesarAsync(ctx,
                RawInbound(pid, "payment.outbound.settled", "SETTLED"));

            Assert.Equal(PassportWebhookProcessResult.NotInbound, r.Result);
            var fila = Assert.Single(await Filas(ctx, pid));
            Assert.False(fila.Processed);
            Assert.Null(fila.FechaProcesado);
            Assert.NotNull(fila.ErrorMessage);
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // 6 — evento DESCONOCIDO: manejo seguro y explícito, sin procesar.
    [Fact]
    public async Task EventoDesconocido_SeRegistraSinProcesar()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);

            var r = await ProcesarAsync(ctx, RawInbound(pid, "key.updated", "X"));

            Assert.Equal(PassportWebhookProcessResult.UnknownEvent, r.Result);
            Assert.False(Assert.Single(await Filas(ctx, pid)).Processed);
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // 7 — CONCURRENCIA. Documenta el RIESGO RESIDUAL: la tabla no tiene índice
    // único sobre (event_type, passport_payment_id) —y XPAY-594 no lo añade—, de
    // modo que dos entregas simultáneas del MISMO evento pueden llegar a
    // insertar dos filas. No se afirma protección que no existe: se acota el
    // daño (sin excepciones, sin doble efecto financiero — no hay ninguno) y se
    // deja constancia ejecutable de la limitación.
    [Fact]
    public async Task Concurrencia_RiesgoResidualAcotado_SinIndiceUnico()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            var raw = RawInbound(pid, PassportInboundEventClassifier.EventInboundReceived, "PROCESSING");

            await using var ctxA = NuevoContexto(cs);
            await using var ctxB = NuevoContexto(cs);

            var resultados = await Task.WhenAll(
                ProcesarAsync(ctxA, raw),
                ProcesarAsync(ctxB, raw));

            // Nunca lanza, y al menos una entrega queda procesada.
            Assert.Contains(resultados, r => r.Result == PassportWebhookProcessResult.Processed);

            await using var ctx = NuevoContexto(cs);
            var filas = await Filas(ctx, pid);

            // Con constraint único sería exactamente 1. Sin él, 1 o 2 son
            // posibles: eso ES la limitación residual documentada.
            Assert.InRange(filas.Count, 1, 2);
            Assert.All(filas, f => Assert.Null(f.IdBrebRetiro));
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // =======================================================================
    // XPAY-602 — entregas en LOTE (raíz array). Misma clave de idempotencia
    // por evento: (event_type, passport_payment_id).
    // =======================================================================

    private static string RawBatch(string paymentId, params (string Evento, string Status)[] eventos)
        => "[" + string.Join(",", eventos.Select(e => RawInbound(paymentId, e.Evento, e.Status))) + "]";

    private static async Task<IReadOnlyList<PassportWebhookProcessOutcome>> ProcesarLoteAsync(
        XpayDbContext ctx, string raw)
    {
        var shape = PassportWebhookPayloadParser.TryParseDelivery(raw, out var payloads);
        Assert.Equal(PassportWebhookDeliveryShape.Batch, shape);
        return await Servicio(ctx).ProcessDeliveryAsync(raw, payloads, signatureValid: true);
    }

    // Caso 4 — received -> confirmed -> settled del MISMO payment_id en un solo
    // lote: son tres entregas legítimamente distintas y las tres se procesan.
    [Fact]
    public async Task Caso04_LoteConTresEstadosDelMismoPago_ProcesaLosTres()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);
            var raw = RawBatch(pid,
                (PassportInboundEventClassifier.EventInboundReceived,  "PROCESSING"),
                (PassportInboundEventClassifier.EventInboundConfirmed, "CONFIRMED"),
                (PassportInboundEventClassifier.EventInboundSettled,   "SETTLED"));

            var r = await ProcesarLoteAsync(ctx, raw);

            Assert.Equal(3, r.Count);
            Assert.All(r, x => Assert.Equal(PassportWebhookProcessResult.Processed, x.Result));
            Assert.Equal(3, (await Filas(ctx, pid)).Count);
            Assert.All(await Filas(ctx, pid), f => { Assert.True(f.Processed); Assert.Null(f.IdBrebRetiro); });
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // Caso 5 — el MISMO (event_type, payment_id) repetido DENTRO del lote:
    // el primero se procesa, el segundo es duplicado idempotente.
    [Fact]
    public async Task Caso05_DuplicadoExactoDentroDelLote_EsIdempotente()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);
            var raw = RawBatch(pid,
                (PassportInboundEventClassifier.EventInboundReceived, "PROCESSING"),
                (PassportInboundEventClassifier.EventInboundReceived, "PROCESSING"));

            var r = await ProcesarLoteAsync(ctx, raw);

            Assert.Equal(2, r.Count);
            Assert.Equal(PassportWebhookProcessResult.Processed, r[0].Result);
            Assert.Equal(PassportWebhookProcessResult.Duplicate, r[1].Result);
            Assert.Single(await Filas(ctx, pid));
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // Caso 13 — reintento del MISMO lote completo (Delivery Mode=Retry):
    // ninguna fila nueva, todo duplicado.
    [Fact]
    public async Task Caso13_ReintentoDelMismoLote_NoDuplicaFilas()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);
            var raw = RawBatch(pid,
                (PassportInboundEventClassifier.EventInboundReceived,  "PROCESSING"),
                (PassportInboundEventClassifier.EventInboundConfirmed, "CONFIRMED"));

            var primera = await ProcesarLoteAsync(ctx, raw);
            var reintento = await ProcesarLoteAsync(ctx, raw);

            Assert.All(primera,  x => Assert.Equal(PassportWebhookProcessResult.Processed, x.Result));
            Assert.All(reintento, x => Assert.Equal(PassportWebhookProcessResult.Duplicate, x.Result));
            Assert.Equal(2, (await Filas(ctx, pid)).Count);
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // Caso 6 (parte DB) — salientes dentro del lote: se registran para
    // auditoría, NO se procesan y no producen ningún efecto financiero.
    [Fact]
    public async Task Caso06_SalientesEnElLote_SeRegistranSinProcesar()
    {
        if (!TryConnString(out var cs)) return;
        var pid = NuevoPaymentId();
        try
        {
            await using var ctx = NuevoContexto(cs);
            var raw = RawBatch(pid,
                (PassportInboundEventClassifier.EventInboundReceived, "PROCESSING"),
                ("payment.outbound.settled", "SETTLED"),
                ("payment.outbound.confirmed", "CONFIRMED"));

            var r = await ProcesarLoteAsync(ctx, raw);

            Assert.Equal(PassportWebhookProcessResult.Processed,  r[0].Result);
            Assert.Equal(PassportWebhookProcessResult.NotInbound, r[1].Result);
            Assert.Equal(PassportWebhookProcessResult.NotInbound, r[2].Result);

            var filas = await Filas(ctx, pid);
            Assert.Equal(3, filas.Count);
            Assert.Equal(1, filas.Count(f => f.Processed));
            // Caso 15 — ningún evento del lote se correlaciona con un retiro,
            // así que no existe vía por la que tocar Wallet/Ledger/breb_retiros.
            Assert.All(filas, f => Assert.Null(f.IdBrebRetiro));
        }
        finally { await LimpiarAsync(cs, pid); }
    }

    // --- Utilidades ---------------------------------------------------------

    private static string NuevoPaymentId() => "xpay594-" + Guid.NewGuid().ToString("N")[..20];

    private static async Task<List<Xpay.Api.Models.PassportWebhookEvent>> Filas(
        XpayDbContext ctx, string pid)
        => await ctx.PassportWebhookEvents.AsNoTracking()
            .Where(e => e.PassportPaymentId == pid)
            .OrderBy(e => e.IdEvent)
            .ToListAsync();

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi,
            $"{EnvConnString} es obligatoria en CI para las pruebas SQL de idempotencia de M5-T6/T7.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, string pid)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM passport_webhook_events WHERE passport_payment_id = {0}", pid);
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}

// TimeProvider fijo local: FijoTimeProvider ya existe en
// CarteraConsultaRiesgoServiceTests dentro de este mismo namespace.
