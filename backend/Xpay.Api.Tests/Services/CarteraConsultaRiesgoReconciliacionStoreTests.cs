using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// M2.4d — reconciliación fail-closed de una consulta de riesgo ATASCADA
// (CarteraConsultaRiesgoReconciliacionStore). Integración SQL contra el SQL
// Server efímero del pipeline. SIN red, SIN proveedor. Guard fail-closed
// idéntico a M2.4a/b/c: local sin ConnectionStrings__XpayConnection →
// early-return (PASS) ; en CI sin la variable → FALLA.
// ══════════════════════════════════════════════════════════════════════════

[Collection("SqlIntegration")]
public sealed class CarteraConsultaRiesgoReconciliacionStoreTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // 20 — sin proveedor: el store SÓLO depende de XpayDbContext (estructuralmente
    // imposible que llame a MiDecisor). Test PURO (sin SQL).
    [Fact]
    public void Store_no_tiene_dependencia_de_proveedor()
    {
        var ctor = typeof(CarteraConsultaRiesgoReconciliacionStore).GetConstructors().Single();
        var tipos = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Single(tipos);
        Assert.Equal(typeof(XpayDbContext), tipos[0]);
    }

    // 16 / 17 ───────────────────────────────────────────────────────────
    [Theory]
    [InlineData(CarteraIntentoFases.PreCall)]
    [InlineData(CarteraIntentoFases.EnvioIncierto)]
    public async Task ConsultandoRiesgo_atascada_se_cierra_ErrorProveedor_ResultadoIncierto(string fase)
    {
        if (!TryConnString(out var cs)) return;

        var pe = new List<long>(); var us = new List<long>(); var so = new List<long>();
        try
        {
            var (idSolicitud, _) = await SembrarAtascadaAsync(cs, fase, pe, us, so);

            await using var ctx = NuevoContexto(cs);
            var store = new CarteraConsultaRiesgoReconciliacionStore(ctx);
            var r = await store.ReconciliarConsultaAtascadaAsync(idSolicitud);

            Assert.Equal(ResultadoReconciliacionConsulta.Reconciliada, r);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == idSolicitud);
            var intento = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(x => x.IdSolicitud == idSolicitud && x.NumeroIntento == 1);

            Assert.Equal(CarteraSolicitudCupoEstados.ErrorProveedor, sol.EstadoSolicitud);
            Assert.Equal(CarteraConsultaRiesgoResultados.ResultadoIncierto, intento.ResultadoTecnico);
            Assert.Equal(CarteraIntentoFases.Finalizado, intento.FaseIntento);
            Assert.False(intento.EsIntentoConResultadoUtil);
            Assert.NotNull(intento.FechaFin);
            Assert.Null(intento.ScoreRaw);
            Assert.Null(intento.ResultadoConsumidoUtc);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // 18 / E — repetición tras reconciliar: 1ª → Reconciliada ; 2ª → SolicitudYaCerrada
    [Fact]
    public async Task Reconciliacion_repetida_es_idempotente()
    {
        if (!TryConnString(out var cs)) return;

        var pe = new List<long>(); var us = new List<long>(); var so = new List<long>();
        try
        {
            var (idSolicitud, _) = await SembrarAtascadaAsync(cs, CarteraIntentoFases.EnvioIncierto, pe, us, so);

            await using (var c1 = NuevoContexto(cs))
                Assert.Equal(ResultadoReconciliacionConsulta.Reconciliada,
                    await new CarteraConsultaRiesgoReconciliacionStore(c1).ReconciliarConsultaAtascadaAsync(idSolicitud));

            await using (var c2 = NuevoContexto(cs))
                Assert.Equal(ResultadoReconciliacionConsulta.SolicitudYaCerrada,
                    await new CarteraConsultaRiesgoReconciliacionStore(c2).ReconciliarConsultaAtascadaAsync(idSolicitud));

            await using var v = NuevoContexto(cs);
            var n = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .CountAsync(x => x.IdSolicitud == idSolicitud);
            Assert.Equal(1, n);
            var intento = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(x => x.IdSolicitud == idSolicitud && x.NumeroIntento == 1);
            Assert.Equal(CarteraConsultaRiesgoResultados.ResultadoIncierto, intento.ResultadoTecnico);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // 19 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Estado_no_elegible_no_se_modifica()
    {
        if (!TryConnString(out var cs)) return;

        var pe = new List<long>(); var us = new List<long>(); var so = new List<long>();
        try
        {
            // EN_EVALUACION con intento FINALIZADO — NO reconciliable.
            var (idSolicitud, _) = await SembrarAsync(cs, pe, us, so,
                estadoSolicitud: CarteraSolicitudCupoEstados.EnEvaluacion,
                faseIntento: CarteraIntentoFases.Finalizado,
                resultadoTecnico: CarteraConsultaRiesgoResultados.Aceptada);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoReconciliacionStore(ctx).ReconciliarConsultaAtascadaAsync(idSolicitud);

            Assert.Equal(ResultadoReconciliacionConsulta.NoElegible, r);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == idSolicitud);
            Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, sol.EstadoSolicitud);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // C — ERROR_PROVEEDOR alcanzado por TX-B (falla real del proveedor, NO
    // reconciliación): intento FINALIZADO coherente → SolicitudYaCerrada, y
    // NUNCA "Reconciliada". 0 writes: resultado_tecnico original intacto.
    [Fact]
    public async Task ErrorProveedor_por_TXB_no_se_etiqueta_como_reconciliada()
    {
        if (!TryConnString(out var cs)) return;

        var pe = new List<long>(); var us = new List<long>(); var so = new List<long>();
        try
        {
            var (idSolicitud, _) = await SembrarAsync(cs, pe, us, so,
                estadoSolicitud: CarteraSolicitudCupoEstados.ErrorProveedor,
                faseIntento: CarteraIntentoFases.Finalizado,
                resultadoTecnico: CarteraConsultaRiesgoResultados.ErrorAutenticacion);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoReconciliacionStore(ctx).ReconciliarConsultaAtascadaAsync(idSolicitud);

            Assert.Equal(ResultadoReconciliacionConsulta.SolicitudYaCerrada, r);
            Assert.NotEqual(ResultadoReconciliacionConsulta.Reconciliada, r);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == idSolicitud);
            var intento = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(x => x.IdSolicitud == idSolicitud && x.NumeroIntento == 1);
            Assert.Equal(CarteraSolicitudCupoEstados.ErrorProveedor, sol.EstadoSolicitud);
            Assert.Equal(CarteraConsultaRiesgoResultados.ErrorAutenticacion, intento.ResultadoTecnico); // sin cambios
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // D — ERROR_PROVEEDOR con intento incoherente (no FINALIZADO) → NoElegible,
    // sin tocar nada.
    [Fact]
    public async Task ErrorProveedor_con_intento_incoherente_NoElegible()
    {
        if (!TryConnString(out var cs)) return;

        var pe = new List<long>(); var us = new List<long>(); var so = new List<long>();
        try
        {
            var (idSolicitud, _) = await SembrarAsync(cs, pe, us, so,
                estadoSolicitud: CarteraSolicitudCupoEstados.ErrorProveedor,
                faseIntento: CarteraIntentoFases.PreCall,
                resultadoTecnico: null);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoReconciliacionStore(ctx).ReconciliarConsultaAtascadaAsync(idSolicitud);

            Assert.Equal(ResultadoReconciliacionConsulta.NoElegible, r);

            await using var v = NuevoContexto(cs);
            var intento = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(x => x.IdSolicitud == idSolicitud && x.NumeroIntento == 1);
            Assert.Equal(CarteraIntentoFases.PreCall, intento.FaseIntento); // sin cambios
            Assert.Null(intento.ResultadoTecnico);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── siembra ─────────────────────────────────────────────────────────
    private static Task<(long idSolicitud, long idUsuario)> SembrarAtascadaAsync(
        string cs, string fase, List<long> pe, List<long> us, List<long> so)
        => SembrarAsync(cs, pe, us, so,
            estadoSolicitud: CarteraSolicitudCupoEstados.ConsultandoRiesgo,
            faseIntento: fase,
            resultadoTecnico: null);

    private static async Task<(long idSolicitud, long idUsuario)> SembrarAsync(
        string cs, List<long> pe, List<long> us, List<long> so,
        string estadoSolicitud, string faseIntento, string? resultadoTecnico)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var idUnidad = await ctx.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
        var idPolitica = await ctx.CarteraPoliticasCredito.AsNoTracking()
            .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
            NumeroDocumento = $"77{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
            PrimerNombre = "RecTest", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        pe.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"rec_test_{sufijo}",
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        us.Add(usuario.IdUsuario);

        var solicitud = new CarteraSolicitudCupo
        {
            IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona, MontoSolicitado = 500_000m,
            EstadoSolicitud = estadoSolicitud, DecisionCrediticia = CarteraDecisionCrediticia.Pendiente,
            IdPoliticaAplicada = idPolitica, CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
            EdadMinimaAplicada = 18, EdadMaximaAplicada = 99, NumeroIntento = 1,
            CorrelationId = $"rec-sol-{sufijo}", FechaSolicitud = ahora, FechaActualizacion = ahora,
        };
        ctx.CarteraSolicitudesCupo.Add(solicitud);
        await ctx.SaveChangesAsync();
        so.Add(solicitud.IdSolicitud);

        ctx.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
        {
            IdSolicitud = solicitud.IdSolicitud, NumeroIntento = 1, IdempotencyKey = Guid.NewGuid(),
            FechaInicio = ahora.AddMinutes(-2),
            FechaFin = resultadoTecnico is null ? null : ahora.AddMinutes(-1),
            ResultadoTecnico = resultadoTecnico,
            HttpStatusObservado = resultadoTecnico is null ? null : 200,
            CorrelationId = $"rec-int-{sufijo}",
            EsIntentoConResultadoUtil = resultadoTecnico == CarteraConsultaRiesgoResultados.Aceptada,
            FaseIntento = faseIntento,
        });
        await ctx.SaveChangesAsync();

        return (solicitud.IdSolicitud, usuario.IdUsuario);
    }

    // ── infraestructura de test (mismo patrón que M2.4b/c) ───────────────
    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de reconciliación de M2.4d.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, List<long> personas, List<long> usuarios, List<long> solicitudes)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud = {id}");
            foreach (var id in solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_solicitud = {id}");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch
        {
            // Limpieza best-effort — el siguiente run usa sufijos únicos.
        }
    }
}
