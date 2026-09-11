using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// XPAY-213/214 — integración SQL de la política de retención B4 (XPAY-212
// §Q.8): purga ATÓMICA de los 7 crudos del intento + los 9 crudos
// RAW/VERBATIM_PROVIDER del snapshot P0 (SCOPE_OPTION_C), con
// MANUAL_REVIEW_HOLD y marca durable p0_raw_purgado_utc (migración 042).
//
// SIN red, SIN proveedor real, SIN token, SIN cédulas. El primitivo
// (CarteraConsultaRiesgoStore.PurgarConsultaRiesgoCompletaAsync /
// ICarteraResultadoRiesgoPurga) NO tiene caller de runtime — el endpoint admin
// queda implementado pero NO invocado en XPAY-214/216; estos tests lo
// instancian explícitamente. El batch runner (CarteraConsultaRiesgoPurgaBatchRunner)
// SÍ se instancia directamente en esta clase desde XPAY-216, exclusivamente
// para probar su gate de activación fail-closed (ver sección "gate de
// activación" más abajo) — nunca para ejecutar una purga real.
//
// Local sin `ConnectionStrings__XpayConnection`: los [Fact]/[Theory] SQL
// retornan temprano y CUENTAN COMO PASS. En CI la variable la fija
// `dotnet test`; si faltara, el test FALLA (no falso verde). Reutiliza la
// colección SqlIntegration definida en CarteraConsultaRiesgoConcurrencyTests.
// Los tests del gate deshabilitado (A/B/valor no reconocido) NO dependen de
// conexión SQL — corren siempre, en cualquier entorno.
// ══════════════════════════════════════════════════════════════════════════

[Collection("SqlIntegration")]
public sealed class CarteraConsultaRiesgoPurgaB4Tests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    private const string ScoreRaw      = "777";
    private const string ViabilidadRaw = "ALTA";
    private const string RatingRaw     = "A";
    private const string MontoRaw      = "1500000";
    private const string P0Json        = "{\"comportamiento\":[]}"; // sintético, no derivado de proveedor real

    // ── 1 — <5 años → NoElegible ───────────────────────────────────────────
    [Fact]
    public async Task MenosDe5Anios_NoElegible()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddYears(1); // 4 años atrás respecto a "ahora" ⇒ NO vencido
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts { FechaFin = fechaFin }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);

            Assert.Equal(ResultadoPurgaConsultaCompleta.NoElegible, r);
            await AssertSinCambiosAsync(cs, idSolicitud);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 2 — FechaFin == cutoff (boundary) → NoElegible ─────────────────────
    [Fact]
    public async Task FechaFin_IgualACutoff_NoElegible()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts { FechaFin = cutoff }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);

            Assert.Equal(ResultadoPurgaConsultaCompleta.NoElegible, r); // estricto: FechaFin >= cutoff ⇒ NO elegible
            await AssertSinCambiosAsync(cs, idSolicitud);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 3 — FechaFin < cutoff → Purgado ─────────────────────────────────────
    [Fact]
    public async Task FechaFin_MenorACutoff_Purgado()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddSeconds(-1);
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, new SeedOpts { FechaFin = fechaFin }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);

            Assert.Equal(ResultadoPurgaConsultaCompleta.Purgado, r);

            await using var v = NuevoContexto(cs);
            var it  = await v.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            Assert.NotNull(it.ResultadoPurgadoUtc);
            Assert.NotNull(sol.P0RawPurgadoUtc);
            Assert.Equal(it.ResultadoPurgadoUtc, sol.P0RawPurgadoUtc); // mismo nowUtc para ambas marcas
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 4 — PENDIENTE_REVISION_MANUAL vencido → RetenidoPorRevisionManual ──
    [Fact]
    public async Task PendienteRevisionManual_Vencido_RetenidoPorRevisionManual()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddYears(-1); // muy vencido
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts
            {
                FechaFin        = fechaFin,
                EstadoSolicitud = CarteraSolicitudCupoEstados.PendienteRevisionManual,
                DecisionCrediticia = CarteraDecisionCrediticia.NoDecidible,
                CodigoMotivoDecision = CarteraMotivoDecision.EdadRequiereRevisionManual,
                FechaDecision   = fechaFin.AddMinutes(5),
            }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);

            Assert.Equal(ResultadoPurgaConsultaCompleta.RetenidoPorRevisionManual, r);
            await AssertSinCambiosAsync(cs, idSolicitud);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 5 — sale de revisión, vencido → Purgado sin cambiar FechaFin ───────
    [Fact]
    public async Task SaleDeRevisionManual_Vencido_Purgado_FechaFinNoSeReinicia()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddYears(-1);
            // Simula "salió de PENDIENTE_REVISION_MANUAL" sembrando directamente
            // el estado terminal resuelto (el workflow de resolución manual no
            // está implementado — fuera de alcance de XPAY-214).
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts
            {
                FechaFin        = fechaFin,
                EstadoSolicitud = CarteraSolicitudCupoEstados.Rechazada,
                DecisionCrediticia = CarteraDecisionCrediticia.Rechazada,
                CodigoMotivoDecision = CarteraMotivoDecision.ScoreInsuficiente,
                FechaDecision   = fechaFin.AddMinutes(5),
            }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);

            Assert.Equal(ResultadoPurgaConsultaCompleta.Purgado, r);

            await using var v = NuevoContexto(cs);
            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i => i.IdSolicitud == idSolicitud);
            Assert.Equal(fechaFin, it.FechaFin); // el reloj NO se reinicia
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 6/7/8 — campos correctos NULL / permanecen ─────────────────────────
    [Fact]
    public async Task Purgado_7CrudosIntentoNull_9CrudosP0Null_3MetadatosPermanecen()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddSeconds(-1);
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, new SeedOpts { FechaFin = fechaFin }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);
            Assert.Equal(ResultadoPurgaConsultaCompleta.Purgado, r);

            await using var v = NuevoContexto(cs);
            var it  = await v.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);

            // 7 — intento
            Assert.Null(it.ConInformacion);
            Assert.Null(it.ScoreRaw);
            Assert.Null(it.ViabilidadRaw);
            Assert.Null(it.RatingRecaudosRaw);
            Assert.Null(it.MontoSugeridoRaw);
            Assert.Null(it.AlertasCount);
            Assert.Null(it.P0ProviderRawJson);

            // 8 — 9 crudos P0
            Assert.Null(sol.TipoDocumentoObservado);
            Assert.Null(sol.EstadoDocumentoDatosBasicosRaw);
            Assert.Null(sol.EstadoDocumentoInfoDemograficaRaw);
            Assert.Null(sol.RangoEdadDatosBasicosRaw);
            Assert.Null(sol.RangoEdadInfoDemograficaRaw);
            Assert.Null(sol.ConsultaAnioRaw);
            Assert.Null(sol.ConsultaMesRaw);
            Assert.Null(sol.ConsultaDiaRaw);
            Assert.Null(sol.ComportamientoVectorJson);

            // 9 — 3 metadatos derivados PERMANECEN
            Assert.Equal("PRESENTE", sol.EstadoDocumentoCaptura);
            Assert.Equal("PRESENTE", sol.RangoEdadCaptura);
            Assert.Equal(6, sol.ComportamientoVectorCount);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 9 — decisión/motivos/cupo permanecen ───────────────────────────────
    [Fact]
    public async Task Purgado_DecisionMotivosCupoPermanecen()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddSeconds(-1);
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts
            {
                FechaFin           = fechaFin,
                EstadoSolicitud    = CarteraSolicitudCupoEstados.Rechazada,
                DecisionCrediticia = CarteraDecisionCrediticia.Rechazada,
                CodigoMotivoDecision = CarteraMotivoDecision.ScoreInsuficiente,
                FechaDecision      = fechaFin.AddMinutes(5),
                ConMotivo          = true,
            }, creados);

            await using var preCtx = NuevoContexto(cs);
            var motivosAntes = await preCtx.Set<CarteraSolicitudCupoMotivoDecision>().AsNoTracking()
                .Where(m => m.IdSolicitud == idSolicitud).ToListAsync();

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);
            Assert.Equal(ResultadoPurgaConsultaCompleta.Purgado, r);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            var motivosDespues = await v.Set<CarteraSolicitudCupoMotivoDecision>().AsNoTracking()
                .Where(m => m.IdSolicitud == idSolicitud).ToListAsync();

            Assert.Equal(CarteraSolicitudCupoEstados.Rechazada, sol.EstadoSolicitud);
            Assert.Equal(CarteraDecisionCrediticia.Rechazada, sol.DecisionCrediticia);
            Assert.Equal(CarteraMotivoDecision.ScoreInsuficiente, sol.CodigoMotivoDecision);
            Assert.NotNull(sol.FechaDecision);
            Assert.Single(motivosAntes);
            Assert.Single(motivosDespues);
            Assert.Equal(motivosAntes[0].CodigoMotivo, motivosDespues[0].CodigoMotivo);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 10 — segunda ejecución idempotente ─────────────────────────────────
    [Fact]
    public async Task SegundaEjecucion_YaPurgado_SinCambios()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddSeconds(-1);
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, new SeedOpts { FechaFin = fechaFin }, creados);

            await using (var c1 = NuevoContexto(cs))
                Assert.Equal(ResultadoPurgaConsultaCompleta.Purgado,
                    await new CarteraConsultaRiesgoStore(c1).PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default));

            DateTime? marcaTrasPrimera;
            await using (var v1 = NuevoContexto(cs))
                marcaTrasPrimera = (await v1.CarteraSolicitudCupoIntentos.AsNoTracking()
                    .SingleAsync(i => i.IdSolicitud == idSolicitud)).ResultadoPurgadoUtc;

            await using (var c2 = NuevoContexto(cs))
                Assert.Equal(ResultadoPurgaConsultaCompleta.YaPurgado,
                    await new CarteraConsultaRiesgoStore(c2).PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default));

            await using var v = NuevoContexto(cs);
            var it  = await v.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            var n   = await v.CarteraSolicitudCupoIntentos.AsNoTracking().CountAsync(i => i.IdSolicitud == idSolicitud);

            Assert.Equal(1, n); // sin filas duplicadas
            Assert.Equal(marcaTrasPrimera, it.ResultadoPurgadoUtc); // la marca no cambia en la 2ª ejecución
            Assert.Equal(it.ResultadoPurgadoUtc, sol.P0RawPurgadoUtc);
            Assert.Null(it.ScoreRaw);
            Assert.Null(sol.TipoDocumentoObservado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 11 — concurrencia ───────────────────────────────────────────────────
    [Fact]
    public async Task Concurrencia_UnaSolaPurgaReal_LaOtraYaPurgado()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddSeconds(-1);
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts { FechaFin = fechaFin }, creados);

            await using var c1 = NuevoContexto(cs);
            await using var c2 = NuevoContexto(cs);

            var t1 = new CarteraConsultaRiesgoStore(c1).PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);
            var t2 = new CarteraConsultaRiesgoStore(c2).PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);

            var resultados = await Task.WhenAll(t1, t2);

            Assert.Contains(ResultadoPurgaConsultaCompleta.Purgado, resultados);
            Assert.Contains(ResultadoPurgaConsultaCompleta.YaPurgado, resultados);

            await using var v = NuevoContexto(cs);
            var n = await v.CarteraSolicitudCupoIntentos.AsNoTracking().CountAsync(i => i.IdSolicitud == idSolicitud);
            Assert.Equal(1, n); // sin corrupción / sin fila duplicada
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 12 — ante estado inconsistente detectado, CERO escrituras (fail-closed
    //         antes de mutar nada — una vez que SaveChanges/Commit corren,
    //         ambas marcas se escriben atómicamente en la misma transacción;
    //         no existe una ejecución real de "crash a mitad de camino" que un
    //         test de integración pueda inyectar sin herramientas de chaos
    //         engineering, así que esta prueba demuestra la garantía
    //         equivalente y verificable: la invariante se detecta y aborta
    //         ANTES de tocar ninguna fila) ─────────────────────────────────
    [Fact]
    public async Task EstadoInconsistente_CeroEscrituras_RollbackTotal()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddSeconds(-1);
            // Intento ya marcado purgado (resultado_purgado_utc != NULL) pero
            // conserva un crudo — estado imposible bajo el diseño de XPAY-214,
            // simulado directamente en el seed.
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts
            {
                FechaFin = fechaFin,
                ResultadoPurgadoUtcPreset = DateTime.UtcNow.AddDays(-1),
                // P0RawPurgadoUtcPreset queda NULL a propósito ⇒ marcas en desacuerdo.
            }, creados);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraPurgaB4InvarianteException>(
                () => new CarteraConsultaRiesgoStore(ctx).PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default));

            // CERO escrituras: exactamente el mismo estado sembrado.
            await using var v = NuevoContexto(cs);
            var it  = await v.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i => i.IdSolicitud == idSolicitud);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            Assert.NotNull(it.ResultadoPurgadoUtc);
            Assert.Equal(ScoreRaw, it.ScoreRaw); // el crudo "inconsistente" sigue intacto, no se tocó
            Assert.Null(sol.P0RawPurgadoUtc);
            Assert.NotNull(sol.TipoDocumentoObservado); // el P0 tampoco se tocó
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 13 — correlación solicitud.numero_intento ↔ intento gobernante ─────
    [Fact]
    public async Task Correlacion_UsaNumeroIntentoDeLaSolicitud_NoOtroIntentoDecoy()
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff        = DateTime.UtcNow.AddYears(-5);
            var fechaFinVieja = cutoff.AddSeconds(-1);   // intento #1 (gobernante) — vencido
            var fechaFinNueva = DateTime.UtcNow;         // intento #2 (decoy) — NO vencido

            // solicitud.NumeroIntento queda en 1 (default) ⇒ el intento
            // gobernante es (idSolicitud, 1), NO el decoy (idSolicitud, 2).
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts { FechaFin = fechaFinVieja }, creados);
            await SembrarIntentoDecoyAsync(cs, idSolicitud, numeroIntento: 2, fechaFinNueva);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default);
            Assert.Equal(ResultadoPurgaConsultaCompleta.Purgado, r);

            await using var v = NuevoContexto(cs);
            var gobernante = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == 1);
            var decoy = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == 2);

            Assert.Null(gobernante.ScoreRaw);           // el gobernante SÍ se purgó
            Assert.NotNull(gobernante.ResultadoPurgadoUtc);
            Assert.Equal(ScoreRaw, decoy.ScoreRaw);      // el decoy NUNCA se tocó
            Assert.Null(decoy.ResultadoPurgadoUtc);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 14 — estados parciales de marcas → fail-closed, NO auto-heal ───────
    [Theory]
    [InlineData(true, false)]  // intento purgado, P0 no ⇒ marcas en desacuerdo
    [InlineData(false, true)]  // P0 purgado, intento no ⇒ marcas en desacuerdo
    public async Task MarcasEnDesacuerdo_LanzaInvariante_NoAutoHeal(bool intentoPurgado, bool p0Purgado)
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff   = DateTime.UtcNow.AddYears(-5);
            var fechaFin = cutoff.AddSeconds(-1);
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts
            {
                FechaFin = fechaFin,
                ResultadoPurgadoUtcPreset = intentoPurgado ? DateTime.UtcNow.AddDays(-1) : null,
                P0RawPurgadoUtcPreset     = p0Purgado ? DateTime.UtcNow.AddDays(-1) : null,
                // Si se marca "purgado" pero no se limpia el crudo correspondiente,
                // el estado es estructuralmente imposible bajo este diseño.
                ConCrudosIntento = !intentoPurgado,
                ConCrudosP0      = !p0Purgado,
            }, creados);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraPurgaB4InvarianteException>(
                () => new CarteraConsultaRiesgoStore(ctx).PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 15 (XPAY-216, P1-3) — marcas COINCIDEN entre sí (ambas presentes)
    //         pero uno de los dos lados conserva su propio crudo remanente →
    //         fail-closed, NO auto-heal. Complementa el caso 14 (que cubre
    //         F/G — marcas en DESACUERDO entre sí); éste cubre D/E — marcas
    //         de acuerdo, pero con un lado estructuralmente incompleto ──────
    [Theory]
    [InlineData(true, false)]  // D: intento marcado purgado pero conserva su propio crudo (7 campos)
    [InlineData(false, true)]  // E: P0 marcado purgado pero conserva su propio crudo (9 campos)
    public async Task MarcasCoincidenConCrudoRemanente_LanzaInvariante_NoAutoHeal(
        bool crudoIntentoRemanente, bool crudoP0Remanente)
    {
        if (!TryConnString(out var cs)) return;

        var creados = new Sembrados();
        try
        {
            var cutoff      = DateTime.UtcNow.AddYears(-5);
            var fechaFin     = cutoff.AddSeconds(-1);
            var marcaPreset = DateTime.UtcNow.AddDays(-1);
            var (idSolicitud, _) = await SembrarAsync(cs, new SeedOpts
            {
                FechaFin = fechaFin,
                // Ambas marcas presentes (coinciden entre sí, a diferencia del
                // caso 14) — pero uno de los dos lados no fue realmente
                // limpiado, estado estructuralmente imposible bajo este diseño.
                ResultadoPurgadoUtcPreset = marcaPreset,
                P0RawPurgadoUtcPreset     = marcaPreset,
                ConCrudosIntento = crudoIntentoRemanente,
                ConCrudosP0      = crudoP0Remanente,
            }, creados);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraPurgaB4InvarianteException>(
                () => new CarteraConsultaRiesgoStore(ctx).PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoff, default));

            // CERO escrituras: marcas y crudo remanente permanecen EXACTAMENTE
            // como se sembraron — sin reparación silenciosa del lado incompleto.
            await using var v = NuevoContexto(cs);
            var it  = await v.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i => i.IdSolicitud == idSolicitud);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);

            Assert.Equal(marcaPreset, it.ResultadoPurgadoUtc);
            Assert.Equal(marcaPreset, sol.P0RawPurgadoUtc);
            Assert.Equal(crudoIntentoRemanente ? ScoreRaw : null, it.ScoreRaw);
            Assert.Equal(crudoP0Remanente ? "CC" : null, sol.TipoDocumentoObservado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ══════════════ XPAY-216 (P1-1) — gate de activación del batch runner ═════
    // CarteraConsultaRiesgoPurgaBatchRunner.EjecutarLoteAsync debe bloquear
    // ANTES de tocar la DB o el store cuando el gate CARTERA_PURGE_B4_ENABLED
    // no está explícitamente en "true". Los casos A/B/inválido usan una
    // connection string deliberadamente inalcanzable + un store que lanza si
    // se invoca: si el gate no bloqueara, el test fallaría (no pasaría en
    // silencio). El caso C (habilitado) sí requiere SQL real, porque debe
    // demostrar que el runner efectivamente llega a su lógica normal.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Gate_ConfigAusente_Deshabilitado_SinAccesoADbNiStore()
    {
        await using var ctx = NuevoContextoInalcanzable();
        var runner = new CarteraConsultaRiesgoPurgaBatchRunner(
            ctx, new PurgaNuncaInvocada(), TimeProvider.System,
            ConfigConValor(null), NullLogger<CarteraConsultaRiesgoPurgaBatchRunner>.Instance);

        var resultado = await runner.EjecutarLoteAsync();

        Assert.Equal(CarteraPurgaB4EstadoEjecucion.Deshabilitado, resultado.Estado);
        Assert.Equal(0, resultado.Candidatos);
        Assert.Equal(0, resultado.Purgados);
        Assert.Equal(0, resultado.Errores);
    }

    [Fact]
    public async Task Gate_ConfigFalse_Deshabilitado_SinAccesoADbNiStore()
    {
        await using var ctx = NuevoContextoInalcanzable();
        var runner = new CarteraConsultaRiesgoPurgaBatchRunner(
            ctx, new PurgaNuncaInvocada(), TimeProvider.System,
            ConfigConValor("false"), NullLogger<CarteraConsultaRiesgoPurgaBatchRunner>.Instance);

        var resultado = await runner.EjecutarLoteAsync();

        Assert.Equal(CarteraPurgaB4EstadoEjecucion.Deshabilitado, resultado.Estado);
    }

    // Cualquier valor no reconocido — typo, "1", "yes", cadena vacía — debe
    // ser fail-closed (NO tratarse como habilitado), mismo criterio que
    // ausente/"false".
    [Theory]
    [InlineData("TRUE1")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public async Task Gate_ConfigValorNoReconocido_FailClosed_Deshabilitado(string valorConfig)
    {
        await using var ctx = NuevoContextoInalcanzable();
        var runner = new CarteraConsultaRiesgoPurgaBatchRunner(
            ctx, new PurgaNuncaInvocada(), TimeProvider.System,
            ConfigConValor(valorConfig), NullLogger<CarteraConsultaRiesgoPurgaBatchRunner>.Instance);

        var resultado = await runner.EjecutarLoteAsync();

        Assert.Equal(CarteraPurgaB4EstadoEjecucion.Deshabilitado, resultado.Estado);
    }

    [Fact]
    public async Task Gate_ConfigTrue_ContinuaHaciaLogicaNormal()
    {
        if (!TryConnString(out var cs)) return;

        await using var ctx = NuevoContexto(cs);
        var runner = new CarteraConsultaRiesgoPurgaBatchRunner(
            ctx, new CarteraConsultaRiesgoStore(ctx), TimeProvider.System,
            ConfigConValor("true"), NullLogger<CarteraConsultaRiesgoPurgaBatchRunner>.Instance);

        var resultado = await runner.EjecutarLoteAsync(new CarteraPurgaB4LoteOpciones(BatchSize: 1, MaxBatches: 1));

        // Ejecutado ⇒ el gate lo dejó pasar hasta el final del método normal
        // (ObtenerCandidatosAsync contra SQL real + el bucle completo) — NO el
        // retorno inmediato de CarteraPurgaB4LoteResultado.Deshabilitado().
        Assert.Equal(CarteraPurgaB4EstadoEjecucion.Ejecutado, resultado.Estado);
    }

    // ════════════════════ infraestructura de test ════════════════════════

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de purga B4 (XPAY-213/214).");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // XPAY-216 (P1-1) — DbContext apuntando a un host inexistente con timeout
    // corto. EF Core sólo abre la conexión al ejecutar una consulta, nunca al
    // construir el DbContext, así que esto es seguro de crear en cualquier
    // entorno (no requiere red). Se usa exclusivamente en los tests del gate
    // deshabilitado: si el gate no bloqueara antes de tocar la DB, la
    // consulta fallaría rápido en vez de pasar en silencio.
    private static XpayDbContext NuevoContextoInalcanzable()
        => new(new DbContextOptionsBuilder<XpayDbContext>()
            .UseSqlServer("Server=xpay-gate-test-unreachable-host,1;Database=xpay_gate_test;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True;")
            .Options);

    // XPAY-216 (P1-1) — IConfiguration mínima con una sola clave, para probar
    // GateHabilitado() de forma determinista sin tocar appsettings/ambiente
    // reales. valor=null ⇒ la clave queda ausente (simula "config no
    // definida").
    private static IConfiguration ConfigConValor(string? valor)
    {
        var builder = new ConfigurationBuilder();
        if (valor is not null)
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CarteraConsultaRiesgoPurgaBatchRunner.ConfigKeyHabilitado] = valor,
            });
        return builder.Build();
    }

    // XPAY-216 (P1-1) — prueba negativa: si el batch runner invocara el store
    // de purga pese al gate estar deshabilitado, cualquiera de estos dos
    // métodos lanzaría y el test fallaría en vez de pasar en silencio.
    private sealed class PurgaNuncaInvocada : ICarteraResultadoRiesgoPurga
    {
        public Task<ResultadoPurgaIntento> PurgarResultadoIntentoAsync(
            long idSolicitud, int numeroIntento, DateTime cutoffUtc, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "El store de purga NO debe invocarse cuando el gate CARTERA_PURGE_B4_ENABLED está deshabilitado.");

        public Task<ResultadoPurgaConsultaCompleta> PurgarConsultaRiesgoCompletaAsync(
            long idSolicitud, DateTime cutoffUtc, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "El store de purga NO debe invocarse cuando el gate CARTERA_PURGE_B4_ENABLED está deshabilitado.");
    }

    private static async Task<long> LeerIdUnidadAsync(string cs)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
    }

    private static async Task<long> LeerIdPoliticaActivaAsync(string cs)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.CarteraPoliticasCredito.AsNoTracking()
            .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();
    }

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Solicitudes { get; } = new();
    }

    private sealed class SeedOpts
    {
        public DateTime FechaFin = DateTime.UtcNow.AddYears(-6);
        public string EstadoSolicitud = CarteraSolicitudCupoEstados.EnEvaluacion;
        public string DecisionCrediticia = CarteraDecisionCrediticia.Pendiente;
        public string? CodigoMotivoDecision;
        public DateTime? FechaDecision;
        public bool ConMotivo;
        public bool ConCrudosIntento = true;
        public bool ConCrudosP0 = true;
        public DateTime? ResultadoPurgadoUtcPreset;
        public DateTime? P0RawPurgadoUtcPreset;
    }

    private static async Task<(long idSolicitud, int numeroIntento)> SembrarAsync(string cs, SeedOpts o, Sembrados creados)
    {
        var idUnidad   = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);

        await using var ctx = NuevoContexto(cs);
        var ahora  = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
            NumeroDocumento = $"79{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
            PrimerNombre = "PurgaB4Test", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"purgab4_test_{sufijo}",
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);

        var solicitud = new CarteraSolicitudCupo
        {
            IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona, MontoSolicitado = 500_000m,
            EstadoSolicitud = o.EstadoSolicitud, DecisionCrediticia = o.DecisionCrediticia,
            CodigoMotivoDecision = o.CodigoMotivoDecision, FechaDecision = o.FechaDecision,
            IdPoliticaAplicada = idPolitica, CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
            EdadMinimaAplicada = 18, EdadMaximaAplicada = 99, NumeroIntento = 1,
            CorrelationId = $"purgab4-sol-{sufijo}", FechaSolicitud = ahora, FechaActualizacion = ahora,
            TipoDocumentoObservado            = o.ConCrudosP0 ? "CC" : null,
            EstadoDocumentoDatosBasicosRaw     = o.ConCrudosP0 ? "Vigente" : null,
            EstadoDocumentoInfoDemograficaRaw  = o.ConCrudosP0 ? "Vigente" : null,
            EstadoDocumentoCaptura             = "PRESENTE",
            RangoEdadDatosBasicosRaw           = o.ConCrudosP0 ? "36-45" : null,
            RangoEdadInfoDemograficaRaw        = o.ConCrudosP0 ? "36-45" : null,
            RangoEdadCaptura                   = "PRESENTE",
            ConsultaAnioRaw                    = o.ConCrudosP0 ? "2026" : null,
            ConsultaMesRaw                     = o.ConCrudosP0 ? "9" : null,
            ConsultaDiaRaw                     = o.ConCrudosP0 ? "1" : null,
            ComportamientoVectorJson           = o.ConCrudosP0 ? P0Json : null,
            ComportamientoVectorCount          = 6,
            P0RawPurgadoUtc                    = o.P0RawPurgadoUtcPreset,
        };
        ctx.CarteraSolicitudesCupo.Add(solicitud);
        await ctx.SaveChangesAsync();
        creados.Solicitudes.Add(solicitud.IdSolicitud);

        ctx.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
        {
            IdSolicitud = solicitud.IdSolicitud, NumeroIntento = 1, IdempotencyKey = Guid.NewGuid(),
            FechaInicio = o.FechaFin.AddMinutes(-1), FechaFin = o.FechaFin,
            ResultadoTecnico = CarteraConsultaRiesgoResultados.Aceptada, HttpStatusObservado = 200,
            ContentStatusObservado = "202 ACCEPTED", CorrelationId = $"purgab4-int-{sufijo}",
            EsIntentoConResultadoUtil = true, FaseIntento = CarteraIntentoFases.Finalizado,
            ResultadoConsumidoUtc = o.FechaFin.AddMinutes(1),
            ConInformacion    = o.ConCrudosIntento ? true : null,
            ScoreRaw          = o.ConCrudosIntento ? ScoreRaw : null,
            ViabilidadRaw     = o.ConCrudosIntento ? ViabilidadRaw : null,
            RatingRecaudosRaw = o.ConCrudosIntento ? RatingRaw : null,
            MontoSugeridoRaw  = o.ConCrudosIntento ? MontoRaw : null,
            AlertasCount      = o.ConCrudosIntento ? 0 : null,
            P0ProviderRawJson = o.ConCrudosIntento ? P0Json : null,
            ResultadoPurgadoUtc = o.ResultadoPurgadoUtcPreset,
        });
        await ctx.SaveChangesAsync();

        if (o.ConMotivo)
        {
            ctx.Set<CarteraSolicitudCupoMotivoDecision>().Add(new CarteraSolicitudCupoMotivoDecision
            {
                IdSolicitud = solicitud.IdSolicitud, Orden = 1, CodigoMotivo = o.CodigoMotivoDecision!,
            });
            await ctx.SaveChangesAsync();
        }

        return (solicitud.IdSolicitud, 1);
    }

    // Intento decoy adicional para una solicitud ya sembrada — simula (SIN
    // implementar reconsulta) que pudiera existir físicamente una 2ª fila de
    // intento, para probar que la correlación usa EXCLUSIVAMENTE
    // solicitud.numero_intento y nunca "cualquier intento de la solicitud".
    private static async Task SembrarIntentoDecoyAsync(string cs, long idSolicitud, int numeroIntento, DateTime fechaFin)
    {
        await using var ctx = NuevoContexto(cs);
        ctx.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
        {
            IdSolicitud = idSolicitud, NumeroIntento = numeroIntento, IdempotencyKey = Guid.NewGuid(),
            FechaInicio = fechaFin.AddMinutes(-1), FechaFin = fechaFin,
            ResultadoTecnico = CarteraConsultaRiesgoResultados.Aceptada, HttpStatusObservado = 200,
            ContentStatusObservado = "202 ACCEPTED", CorrelationId = $"purgab4-decoy-{Guid.NewGuid():N}"[..40],
            EsIntentoConResultadoUtil = true, FaseIntento = CarteraIntentoFases.Finalizado,
            ResultadoConsumidoUtc = fechaFin.AddMinutes(1),
            ConInformacion = true, ScoreRaw = ScoreRaw, ViabilidadRaw = ViabilidadRaw,
            RatingRecaudosRaw = RatingRaw, MontoSugeridoRaw = MontoRaw, AlertasCount = 0,
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task AssertSinCambiosAsync(string cs, long idSolicitud)
    {
        await using var v = NuevoContexto(cs);
        var it  = await v.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i => i.IdSolicitud == idSolicitud);
        var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
        Assert.Null(it.ResultadoPurgadoUtc);
        Assert.Equal(ScoreRaw, it.ScoreRaw);
        Assert.Null(sol.P0RawPurgadoUtc);
        Assert.NotNull(sol.TipoDocumentoObservado);
    }

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_motivos_decision WHERE id_solicitud = {id}");
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud = {id}");
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_solicitud = {id}");
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch
        {
            // Limpieza best-effort — el siguiente run usa sufijos únicos.
        }
    }
}
