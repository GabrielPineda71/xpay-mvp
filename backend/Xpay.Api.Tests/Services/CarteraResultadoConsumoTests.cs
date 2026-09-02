using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// M2.4a — consumo durable DORMIDO del resultado MiDecisor.
//
// Parte 1 (CarteraResultadoRiesgoNormalizerTests): tabla de verdad PURA del
// normalizador — sin SQL, sin red. Reglas cerradas en el diseño 109 (PASO 7).
//
// Parte 2 (CarteraResultadoConsumoSqlTests): integración SQL del primitivo
// CarteraConsultaRiesgoStore.ConsumirResultadoRiesgoAsync
// (ICarteraResultadoRiesgoConsumo) contra el SQL Server efímero del pipeline.
// SIN red, SIN proveedor real, SIN token, SIN credenciales, SIN cédulas. El
// primitivo NO tiene caller de runtime; estos tests lo instancian
// explícitamente. Guard fail-closed idéntico a M2.3b2/b3: local sin
// `ConnectionStrings__XpayConnection` → early-return (PASS, no SKIP); en CI
// sin la variable → FALLA (no falso verde). Reutiliza la colección
// SqlIntegration definida en CarteraConsultaRiesgoConcurrencyTests.
// ══════════════════════════════════════════════════════════════════════════

public sealed class CarteraResultadoRiesgoNormalizerTests
{
    private static ResultadoRiesgoNormalizado Norm(
        bool? conInformacion = true, string? score = null, string? viab = null,
        string? rating = null, string? monto = null, int? alertas = null)
        => CarteraResultadoRiesgoNormalizer.Normalizar(conInformacion, score, viab, rating, monto, alertas);

    // ── SCORE ────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("853", 853, CarteraEstadoScore.Disponible)]
    [InlineData("0", 0, CarteraEstadoScore.Disponible)]
    [InlineData("  853  ", 853, CarteraEstadoScore.Disponible)]
    [InlineData("-", null, CarteraEstadoScore.SinDato)]
    [InlineData("", null, CarteraEstadoScore.SinDato)]
    [InlineData(null, null, CarteraEstadoScore.SinDato)]
    [InlineData("abc", null, CarteraEstadoScore.SinDato)]
    [InlineData("8.5", null, CarteraEstadoScore.SinDato)]
    public void Score_conInformacionTrue(string? scoreRaw, int? esperadoScore, string esperadoEstado)
    {
        var r = Norm(conInformacion: true, score: scoreRaw);
        Assert.Equal(esperadoScore, r.Score);
        Assert.Equal(esperadoEstado, r.EstadoScore);
    }

    [Theory]
    [InlineData("99999999999999999999")] // sólo dígitos, desborda Int32
    [InlineData("-5")]                    // negativo bien formado
    public void Score_corrupto_lanzaInvariante(string scoreRaw)
        => Assert.Throws<CarteraConsumoResultadoInvarianteException>(() => Norm(conInformacion: true, score: scoreRaw));

    // ── PRECEDENCIA DE con_informacion (AJUSTE #1) ───────────────────────
    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void ConInformacionNoTrue_anulaTodoElSnapshot_sinInspeccionarLosDemasCrudos(bool? conInformacion)
    {
        // Aun con crudos "buenos" presentes, nada se inspecciona.
        var r = CarteraResultadoRiesgoNormalizer.Normalizar(conInformacion, "853", "ALTA", "A", "13809492", 2);

        Assert.Equal(conInformacion, r.ConInformacion);
        Assert.Null(r.Score);
        Assert.Equal(CarteraEstadoScore.SinInformacion, r.EstadoScore);
        Assert.Null(r.Viabilidad);
        Assert.Null(r.RatingRecaudos);
        Assert.Null(r.MontoSugerido);
        Assert.Null(r.AlertasCount);
    }

    // ── VIABILIDAD ───────────────────────────────────────────────────────
    [Theory]
    [InlineData("ALTA", "ALTA")]
    [InlineData("MEDIA", "MEDIA")]
    [InlineData("BAJA", "BAJA")]
    [InlineData(" ALTA ", "ALTA")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Viabilidad_conInformacionTrue(string? viabRaw, string? esperado)
        => Assert.Equal(esperado, Norm(conInformacion: true, viab: viabRaw).Viabilidad);

    [Theory]
    [InlineData("alta")]
    [InlineData("Alta")]
    [InlineData("XYZ")]
    public void Viabilidad_fueraDeDominio_lanzaInvariante(string viabRaw)
        => Assert.Throws<CarteraConsumoResultadoInvarianteException>(() => Norm(conInformacion: true, viab: viabRaw));

    // ── RATING ───────────────────────────────────────────────────────────
    [Theory]
    [InlineData("A", "A")]
    [InlineData("N", "N")]
    [InlineData("D", "D")]
    [InlineData(" B ", "B")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Rating_conInformacionTrue(string? ratingRaw, string? esperado)
        => Assert.Equal(esperado, Norm(conInformacion: true, rating: ratingRaw).RatingRecaudos);

    [Theory]
    [InlineData("AB")]
    [InlineData("a")]
    [InlineData("z")]
    public void Rating_fueraDeDominio_lanzaInvariante(string ratingRaw)
        => Assert.Throws<CarteraConsumoResultadoInvarianteException>(() => Norm(conInformacion: true, rating: ratingRaw));

    // ── MONTO SUGERIDO ("0" = sin sugerencia → NULL) ─────────────────────
    [Theory]
    [InlineData("13809492", "13809492.00")]
    [InlineData(" 500 ", "500")]
    [InlineData("0", null)]
    [InlineData("00", null)]
    [InlineData("-", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Monto_conInformacionTrue(string? montoRaw, string? esperadoInvariant)
    {
        var r = Norm(conInformacion: true, monto: montoRaw);
        if (esperadoInvariant is null)
            Assert.Null(r.MontoSugerido);
        else
            Assert.Equal(decimal.Parse(esperadoInvariant, CultureInfo.InvariantCulture), r.MontoSugerido);
    }

    [Theory]
    [InlineData("-100")]
    [InlineData("12.50")]
    [InlineData("1,000")]
    [InlineData("99999999999999999999")] // excede DECIMAL(18,2)
    public void Monto_invalido_lanzaInvariante(string montoRaw)
        => Assert.Throws<CarteraConsumoResultadoInvarianteException>(() => Norm(conInformacion: true, monto: montoRaw));

    // ── ALERTAS (passthrough, sin interpretación crediticia) ─────────────
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(null, null)]
    public void Alertas_conInformacionTrue_passthrough(int? alertas, int? esperado)
        => Assert.Equal(esperado, Norm(conInformacion: true, alertas: alertas).AlertasCount);

    // ── CASOS COMBINADOS ─────────────────────────────────────────────────
    [Fact]
    public void Combinado_ACEPTADA_completo()
    {
        var r = CarteraResultadoRiesgoNormalizer.Normalizar(true, "853", "ALTA", "A", "13809492", 2);
        Assert.True(r.ConInformacion);
        Assert.Equal(853, r.Score);
        Assert.Equal(CarteraEstadoScore.Disponible, r.EstadoScore);
        Assert.Equal("ALTA", r.Viabilidad);
        Assert.Equal("A", r.RatingRecaudos);
        Assert.Equal(13809492.00m, r.MontoSugerido);
        Assert.Equal(2, r.AlertasCount);
    }

    [Fact]
    public void Combinado_SIN_INFORMACION_completo()
    {
        var r = CarteraResultadoRiesgoNormalizer.Normalizar(false, "-", null, null, "-", 0);
        Assert.False(r.ConInformacion);
        Assert.Null(r.Score);
        Assert.Equal(CarteraEstadoScore.SinInformacion, r.EstadoScore);
        Assert.Null(r.Viabilidad);
        Assert.Null(r.RatingRecaudos);
        Assert.Null(r.MontoSugerido);
        Assert.Null(r.AlertasCount);
    }
}

[Collection("SqlIntegration")]
public sealed class CarteraResultadoConsumoSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // ── TEST A — consumo elegible ────────────────────────────────────────
    [Fact]
    public async Task Elegible_Consume_EscribeSnapshotYMarcaTimestamp()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica, new SiembraOpts(), creados);

            await using var ctx = NuevoContexto(cs);
            var before = DateTime.UtcNow;
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default);
            var after = DateTime.UtcNow;

            Assert.Equal(ResultadoConsumoRiesgo.Consumido, r);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            Assert.True(sol.ConInformacionObservado);
            Assert.Equal(853, sol.ScoreObservado);
            Assert.Equal(CarteraEstadoScore.Disponible, sol.EstadoScore);
            Assert.Equal("ALTA", sol.ViabilidadObservada);
            Assert.Equal("A", sol.RatingRecaudosObservado);
            Assert.Equal(13809492.00m, sol.MontoSugeridoObservado);
            Assert.Equal(2, sol.AlertasCountObservado);

            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            Assert.NotNull(it.ResultadoConsumidoUtc);
            Assert.True(it.ResultadoConsumidoUtc >= before.AddSeconds(-2), "resultado_consumido_utc anterior al inicio de la operación");
            Assert.True(it.ResultadoConsumidoUtc <= after.AddSeconds(2), "resultado_consumido_utc posterior al fin de la operación");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST B — segundo consumo idempotente ─────────────────────────────
    [Fact]
    public async Task SegundoConsumo_YaConsumido_TimestampYSnapshotNoCambian()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica, new SiembraOpts(), creados);

            await using (var c1 = NuevoContexto(cs))
                Assert.Equal(ResultadoConsumoRiesgo.Consumido,
                    await new CarteraConsultaRiesgoStore(c1).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default));

            DateTime tsConsumo, fechaActualizacion;
            await using (var q = NuevoContexto(cs))
            {
                var it0 = await q.CarteraSolicitudCupoIntentos.AsNoTracking()
                    .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
                tsConsumo = it0.ResultadoConsumidoUtc!.Value;
                fechaActualizacion = (await q.CarteraSolicitudesCupo.AsNoTracking()
                    .SingleAsync(s => s.IdSolicitud == idSolicitud)).FechaActualizacion;
            }

            await using (var c2 = NuevoContexto(cs))
                Assert.Equal(ResultadoConsumoRiesgo.YaConsumido,
                    await new CarteraConsultaRiesgoStore(c2).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default));

            await using var v = NuevoContexto(cs);
            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            Assert.Equal(tsConsumo, it.ResultadoConsumidoUtc);
            Assert.Equal(fechaActualizacion, sol.FechaActualizacion);
            Assert.Equal(853, sol.ScoreObservado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST C — solicitud no EN_EVALUACION ──────────────────────────────
    [Fact]
    public async Task SolicitudNoEnEvaluacion_NoElegible_NadaEscrito()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica,
                new SiembraOpts { EstadoSolicitud = CarteraSolicitudCupoEstados.Recibida }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default);
            Assert.Equal(ResultadoConsumoRiesgo.NoElegible, r);

            await AssertNadaConsumidoAsync(cs, idSolicitud, numeroIntento);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST D/E — fase de intento no FINALIZADO ─────────────────────────
    [Theory]
    [InlineData("PRE_CALL")]
    [InlineData("ENVIO_INCIERTO")]
    public async Task FaseNoFinalizada_NoElegible_NadaEscrito(string fase)
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica,
                new SiembraOpts { Fase = fase, EsUtil = false, ResultadoTecnico = null }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default);
            Assert.Equal(ResultadoConsumoRiesgo.NoElegible, r);

            await AssertNadaConsumidoAsync(cs, idSolicitud, numeroIntento);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST F — intento sin resultado útil ─────────────────────────────
    [Fact]
    public async Task IntentoNoUtil_NoElegible_NadaEscrito()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica,
                new SiembraOpts
                {
                    EsUtil = false,
                    ResultadoTecnico = CarteraConsultaRiesgoResultados.ResultadoIncierto,
                    ConInformacion = null, ScoreRaw = null, ViabilidadRaw = null,
                    RatingRaw = null, MontoRaw = null, AlertasCount = null,
                }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default);
            Assert.Equal(ResultadoConsumoRiesgo.NoElegible, r);

            await AssertNadaConsumidoAsync(cs, idSolicitud, numeroIntento);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST G — intento ya purgado ─────────────────────────────────────
    [Fact]
    public async Task IntentoPurgado_NoElegible_NadaEscrito()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica,
                new SiembraOpts
                {
                    ResultadoPurgadoUtc = DateTime.UtcNow.AddDays(-1),
                    ConInformacion = null, ScoreRaw = null, ViabilidadRaw = null,
                    RatingRaw = null, MontoRaw = null, AlertasCount = null,
                }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default);
            Assert.Equal(ResultadoConsumoRiesgo.NoElegible, r);

            await AssertNadaConsumidoAsync(cs, idSolicitud, numeroIntento);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST H — consumo concurrente sobre el mismo intento ─────────────
    [Fact]
    public async Task ConsumoConcurrenteMismoIntento_ExactamenteUnConsumido_UnUnicoSnapshot()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica, new SiembraOpts(), creados);

            await using var c1 = NuevoContexto(cs);
            await using var c2 = NuevoContexto(cs);
            var res = await Task.WhenAll(
                new CarteraConsultaRiesgoStore(c1).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default),
                new CarteraConsultaRiesgoStore(c2).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default));

            Assert.Equal(1, res.Count(x => x == ResultadoConsumoRiesgo.Consumido));
            Assert.Equal(1, res.Count(x => x == ResultadoConsumoRiesgo.YaConsumido));

            await using var v = NuevoContexto(cs);
            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            Assert.NotNull(it.ResultadoConsumidoUtc);
            Assert.Equal(853, sol.ScoreObservado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST I — dos solicitudes distintas, consumos independientes ─────
    [Fact]
    public async Task ConsumoConcurrenteSolicitudesDistintas_AmbosConsumidos()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var a = await SembrarAsync(cs, idUnidad, idPolitica, new SiembraOpts(), creados);
            var b = await SembrarAsync(cs, idUnidad, idPolitica, new SiembraOpts(), creados);

            await using var c1 = NuevoContexto(cs);
            await using var c2 = NuevoContexto(cs);
            var res = await Task.WhenAll(
                new CarteraConsultaRiesgoStore(c1).ConsumirResultadoRiesgoAsync(a.idSolicitud, a.numeroIntento, default),
                new CarteraConsultaRiesgoStore(c2).ConsumirResultadoRiesgoAsync(b.idSolicitud, b.numeroIntento, default));

            Assert.All(res, x => Assert.Equal(ResultadoConsumoRiesgo.Consumido, x));

            await using var v = NuevoContexto(cs);
            foreach (var (idSol, _) in new[] { a, b })
                Assert.NotNull((await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol)).ScoreObservado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST J — snapshot completo para SIN_INFORMACION ─────────────────
    [Fact]
    public async Task Consume_SinInformacion_SnapshotCompletoYPurgaSeguro()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica,
                new SiembraOpts
                {
                    ResultadoTecnico = CarteraConsultaRiesgoResultados.SinInformacion,
                    ConInformacion = false,
                    ScoreRaw = "-", ViabilidadRaw = null, RatingRaw = null, MontoRaw = "-", AlertasCount = 0,
                }, creados);

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoConsumoRiesgo.Consumido,
                await new CarteraConsultaRiesgoStore(ctx).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            Assert.False(sol.ConInformacionObservado);
            Assert.Null(sol.ScoreObservado);
            Assert.Equal(CarteraEstadoScore.SinInformacion, sol.EstadoScore);
            Assert.Null(sol.ViabilidadObservada);
            Assert.Null(sol.RatingRecaudosObservado);
            Assert.Null(sol.MontoSugeridoObservado);
            Assert.Null(sol.AlertasCountObservado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST K — escrituras de decisión / estado PROHIBIDAS preservadas ─
    [Fact]
    public async Task Consume_NoTocaDecisionNiEstadoNiCrudos()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica, new SiembraOpts(), creados);

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoConsumoRiesgo.Consumido,
                await new CarteraConsultaRiesgoStore(ctx).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, sol.EstadoSolicitud);
            Assert.Equal("PENDIENTE", sol.DecisionCrediticia);
            Assert.Null(sol.MontoAprobado);
            Assert.Null(sol.CodigoMotivoDecision);
            Assert.Null(sol.FechaDecision);
            Assert.Null(sol.IdCupoOrdinario);
            Assert.Null(sol.FechaMaterializacionCupo);
            Assert.Null(sol.EdadCalculadaAlMomento);

            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, it.ResultadoTecnico);
            Assert.Equal(CarteraIntentoFases.Finalizado, it.FaseIntento);
            Assert.True(it.EsIntentoConResultadoUtil);
            Assert.Null(it.ResultadoPurgadoUtc);
            // los 6 crudos intactos
            Assert.True(it.ConInformacion);
            Assert.Equal("853", it.ScoreRaw);
            Assert.Equal("ALTA", it.ViabilidadRaw);
            Assert.Equal("A", it.RatingRecaudosRaw);
            Assert.Equal("13809492", it.MontoSugeridoRaw);
            Assert.Equal(2, it.AlertasCount);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST L — crudo inválido → invariante + rollback durable ─────────
    [Fact]
    public async Task CrudoInvalido_LanzaInvariante_YNoDejaConsumoDurable()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarAsync(cs, idUnidad, idPolitica,
                new SiembraOpts { ViabilidadRaw = "XYZ" }, creados);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraConsumoResultadoInvarianteException>(
                () => new CarteraConsultaRiesgoStore(ctx).ConsumirResultadoRiesgoAsync(idSolicitud, numeroIntento, default));

            await AssertNadaConsumidoAsync(cs, idSolicitud, numeroIntento);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ════════════════════ infraestructura de test ════════════════════════

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;

        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi,
            $"{EnvConnString} es obligatoria en CI para las pruebas SQL de consumo de M2.4a.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

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
            .Where(p => p.Estado == "ACTIVO")
            .OrderBy(p => p.IdPolitica)
            .Select(p => p.IdPolitica)
            .FirstAsync();
    }

    private static async Task AssertNadaConsumidoAsync(string cs, long idSolicitud, int numeroIntento)
    {
        await using var v = NuevoContexto(cs);
        var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
            .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
        Assert.Null(it.ResultadoConsumidoUtc);

        var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
        Assert.Null(sol.ConInformacionObservado);
        Assert.Null(sol.ScoreObservado);
        Assert.Null(sol.EstadoScore);
        Assert.Null(sol.ViabilidadObservada);
        Assert.Null(sol.RatingRecaudosObservado);
        Assert.Null(sol.MontoSugeridoObservado);
        Assert.Null(sol.AlertasCountObservado);
    }

    private sealed class SiembraOpts
    {
        public string Fase { get; set; } = CarteraIntentoFases.Finalizado;
        public bool EsUtil { get; set; } = true;
        public string? ResultadoTecnico { get; set; } = CarteraConsultaRiesgoResultados.Aceptada;
        public string EstadoSolicitud { get; set; } = CarteraSolicitudCupoEstados.EnEvaluacion;
        public bool? ConInformacion { get; set; } = true;
        public string? ScoreRaw { get; set; } = "853";
        public string? ViabilidadRaw { get; set; } = "ALTA";
        public string? RatingRaw { get; set; } = "A";
        public string? MontoRaw { get; set; } = "13809492";
        public int? AlertasCount { get; set; } = 2;
        public DateTime? ResultadoPurgadoUtc { get; set; }
    }

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Solicitudes { get; } = new();
    }

    private static async Task<(long idSolicitud, int numeroIntento)> SembrarAsync(
        string cs, long idUnidad, long idPolitica, SiembraOpts o, Sembrados creados)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var doc = $"76{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}";

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad,
            TipoDocumento   = "CC",
            NumeroDocumento = doc,
            PrimerNombre    = "ConsumoTest",
            PrimerApellido  = "Sintetico",
            Celular         = "3000000000",
            Pais            = "Colombia",
            Estado          = "ACTIVA",
            FechaCreacion   = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona     = persona.IdPersona,
            NombreUsuario = $"consumo_test_{sufijo}",
            PasswordHash  = "x",
            Estado        = "ACTIVO",
            FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);

        var solicitud = new CarteraSolicitudCupo
        {
            IdUsuario          = usuario.IdUsuario,
            IdPersona          = persona.IdPersona,
            MontoSolicitado    = 500_000m,
            EstadoSolicitud    = o.EstadoSolicitud,
            DecisionCrediticia = "PENDIENTE",
            IdPoliticaAplicada = idPolitica,
            CupoMinimoAplicado = 0m,
            CupoMaximoAplicado = 1_000_000m,
            EdadMinimaAplicada = 18,
            EdadMaximaAplicada = 99,
            NumeroIntento      = 1,
            CorrelationId      = $"consumo-sol-{sufijo}",
            FechaSolicitud     = ahora,
            FechaActualizacion = ahora,
        };
        ctx.CarteraSolicitudesCupo.Add(solicitud);
        await ctx.SaveChangesAsync();
        creados.Solicitudes.Add(solicitud.IdSolicitud);

        var esFinalizado = string.Equals(o.Fase, CarteraIntentoFases.Finalizado, StringComparison.Ordinal);

        ctx.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
        {
            IdSolicitud               = solicitud.IdSolicitud,
            NumeroIntento             = 1,
            IdempotencyKey            = Guid.NewGuid(),
            FechaInicio               = ahora.AddMinutes(-2),
            FechaFin                  = esFinalizado ? ahora.AddMinutes(-1) : null,
            ResultadoTecnico          = o.ResultadoTecnico,
            HttpStatusObservado       = esFinalizado ? 200 : null,
            ContentStatusObservado    = esFinalizado ? "202 ACCEPTED" : null,
            CorrelationId             = $"consumo-int-{sufijo}",
            EsIntentoConResultadoUtil = o.EsUtil,
            FaseIntento               = o.Fase,
            ConInformacion            = o.ConInformacion,
            ScoreRaw                  = o.ScoreRaw,
            ViabilidadRaw             = o.ViabilidadRaw,
            RatingRecaudosRaw         = o.RatingRaw,
            MontoSugeridoRaw          = o.MontoRaw,
            AlertasCount              = o.AlertasCount,
            ResultadoPurgadoUtc       = o.ResultadoPurgadoUtc,
        });
        await ctx.SaveChangesAsync();

        return (solicitud.IdSolicitud, 1);
    }

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud = {id}");
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_solicitud = {id}");
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[CarteraResultadoConsumoSqlTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", creados.Personas)}] usuarios=[{string.Join(",", creados.Usuarios)}] " +
                $"solicitudes=[{string.Join(",", creados.Solicitudes)}]");
        }
    }
}
