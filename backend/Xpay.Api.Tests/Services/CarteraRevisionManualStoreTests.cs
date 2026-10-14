using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-702 - revision manual (CarteraRevisionManualStore). Integracion SQL
// contra SQL Server efimero (CI o contenedor local). Sin red, sin proveedor,
// sin cedulas reales. Mismo guard que M2.4b: local sin connection string ->
// early-return; en CI sin ella -> FALLA.
[Collection("SqlIntegration")]
public sealed class CarteraRevisionManualStoreTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    private static string Vec(params (string anioMes, string comp)[] e)
        => JsonSerializer.Serialize(
            e.Select(x => new CarteraComportamientoVectorItemRaw(x.anioMes, x.comp)).ToList());

    private sealed class SeedOpts
    {
        public string  Estado   = CarteraSolicitudCupoEstados.PendienteRevisionManual;
        public string  Decision = CarteraDecisionCrediticia.NoDecidible;
        public int?    Score = 850;
        public string? EstadoScore = CarteraEstadoScore.Disponible;
        public string? VectorJson = Vec(("2026-3", "N"), ("2026-4", "N"), ("2026-5", "N"),
                                        ("2026-6", "N"), ("2026-7", "N"));
        public int?    VectorCount = 5;
    }

    private sealed record Sembrado(long IdSolicitud, long IdUsuario, long IdAdmin);

    private sealed class Limpieza
    {
        public readonly List<long> Personas = new();
        public readonly List<long> Usuarios = new();
        public readonly List<long> Wallets  = new();
    }

    private static async Task<long> NuevoUsuarioAsync(XpayDbContext ctx, long idUnidad, Limpieza l, string prefijo)
    {
        var ahora = DateTime.UtcNow;
        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
            NumeroDocumento = $"77{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
            PrimerNombre = "RevTest", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        l.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona,
            NombreUsuario = $"{prefijo}_{Guid.NewGuid().ToString("N")[..12]}",
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        l.Usuarios.Add(usuario.IdUsuario);
        return usuario.IdUsuario;
    }

    private static async Task<Sembrado> SembrarAsync(string cs, SeedOpts o, Limpieza l)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var idUnidad = await ctx.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
        var idPolitica = await ctx.CarteraPoliticasCredito.AsNoTracking()
            .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();

        var idUsuario = await NuevoUsuarioAsync(ctx, idUnidad, l, "rev_cli");
        var idAdmin   = await NuevoUsuarioAsync(ctx, idUnidad, l, "rev_adm");
        var idPersona = await ctx.Usuarios.Where(u => u.IdUsuario == idUsuario)
            .Select(u => u.IdPersona).SingleAsync();

        var wallet = new Wallet
        {
            IdUnidadNegocio = idUnidad, TipoWallet = "PERSONA", IdPersona = idPersona,
            NombreWallet = $"w_rev_{Guid.NewGuid().ToString("N")[..8]}", Estado = "ACTIVA",
            FechaCreacion = ahora,
        };
        ctx.Wallets.Add(wallet);
        await ctx.SaveChangesAsync();
        l.Wallets.Add(wallet.IdWallet);

        var info = CarteraMotivoDecision.InformacionInsuficiente;
        var solicitud = new CarteraSolicitudCupo
        {
            IdUsuario = idUsuario, IdPersona = idPersona,
            EstadoSolicitud = o.Estado, DecisionCrediticia = o.Decision,
            FechaDecision = ahora.AddMinutes(-1), CodigoMotivoDecision = info,
            IdPoliticaAplicada = idPolitica, CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
            EdadMinimaAplicada = 18, EdadMaximaAplicada = 99, NumeroIntento = 1,
            CorrelationId = $"rev-sol-{Guid.NewGuid().ToString("N")[..12]}",
            FechaSolicitud = ahora, FechaActualizacion = ahora,
            ConInformacionObservado = true, ScoreObservado = o.Score, EstadoScore = o.EstadoScore,
            ViabilidadObservada = "ALTA", RatingRecaudosObservado = "A", TipoDocumentoObservado = "CC",
            EstadoDocumentoInfoDemograficaRaw = "Vigente", EstadoDocumentoCaptura = "PRESENTE",
            RangoEdadInfoDemograficaRaw = "36-45", RangoEdadCaptura = "PRESENTE",
            ComportamientoVectorJson = o.VectorJson, ComportamientoVectorCount = o.VectorCount,
            ConsultaAnioRaw = "2026", ConsultaMesRaw = "9",
        };
        ctx.CarteraSolicitudesCupo.Add(solicitud);
        await ctx.SaveChangesAsync();

        ctx.CarteraSolicitudCupoMotivosDecision.Add(new CarteraSolicitudCupoMotivoDecision
        {
            IdSolicitud = solicitud.IdSolicitud, Orden = 1, CodigoMotivo = info,
        });
        ctx.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
        {
            IdSolicitud = solicitud.IdSolicitud, NumeroIntento = 1, IdempotencyKey = Guid.NewGuid(),
            FechaInicio = ahora.AddMinutes(-3), FechaFin = ahora.AddMinutes(-2),
            ResultadoTecnico = CarteraConsultaRiesgoResultados.Aceptada, HttpStatusObservado = 200,
            ContentStatusObservado = "202 ACCEPTED", CorrelationId = $"rev-int-{Guid.NewGuid().ToString("N")[..12]}",
            EsIntentoConResultadoUtil = true, FaseIntento = CarteraIntentoFases.Finalizado,
            ResultadoConsumidoUtc = ahora.AddMinutes(-1),
        });
        await ctx.SaveChangesAsync();

        return new Sembrado(solicitud.IdSolicitud, idUsuario, idAdmin);
    }

    private static CarteraRevisionManualStore Store(XpayDbContext c)
        => new(c, new CarteraMaterializacionCupoStore(c));

    private static decimal MontoCasoA()
    {
        var p = CarteraPolicyParameters.Vigente();
        var raw = p.BandaS5Base * p.FactorViabilidadAlta * p.FactorRatingA
                * p.FactorComportamientoPeor1 * p.FactorEdadElegible;
        return Math.Min(Math.Ceiling(raw / p.RedondeoUnidad) * p.RedondeoUnidad, p.CupoMaximo);
    }

    [Fact]
    public async Task Aprobar_CasoA_CupoPorPolitica_Materializa_PreservaOriginal()
    {
        if (!TryConnString(out var cs)) return;
        var l = new Limpieza();
        try
        {
            var s = await SembrarAsync(cs, new SeedOpts(), l);
            ResolucionManualResultado r;
            await using (var c = NuevoContexto(cs))
                r = await Store(c).ResolverAsync(s.IdSolicitud, CarteraDecisionCrediticia.Aprobada,
                    CarteraRevisionManualMotivos.AprobacionPolitica, "ok", s.IdAdmin);

            Assert.Equal(ResultadoResolucionManual.Resuelta, r.Resultado);
            Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, r.EstadoSolicitud);
            Assert.Equal(MontoCasoA(), r.MontoAprobado);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal(CarteraDecisionCrediticia.Aprobada, sol.DecisionCrediticia);
            Assert.NotNull(sol.IdCupoOrdinario);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdUsuario == s.IdUsuario);
            Assert.Equal(MontoCasoA(), cupo.CupoAprobado);

            var rev = await v.CarteraSolicitudCupoRevisiones.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal(CarteraDecisionCrediticia.Aprobada, rev.DecisionManual);
            Assert.Equal(MontoCasoA(), rev.MontoAprobadoManual);
            Assert.Equal(CarteraRevisionManualPolicy.ReglaVentanaIncompleta, rev.ReglaAplicada);
            Assert.Equal(s.IdAdmin, rev.IdAdmin);
            Assert.Equal(CarteraDecisionCrediticia.NoDecidible, rev.DecisionAutomaticaOriginal);
            Assert.Equal(CarteraSolicitudCupoEstados.PendienteRevisionManual, rev.EstadoAutomaticoOriginal);
            Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, rev.MotivoAutomaticoOriginal);
            Assert.NotNull(rev.FechaDecisionAutomaticaOriginal);

            var motivos = await v.CarteraSolicitudCupoMotivosDecision.AsNoTracking()
                .Where(m => m.IdSolicitud == s.IdSolicitud).Select(m => m.CodigoMotivo).ToListAsync();
            Assert.Equal(new[] { CarteraMotivoDecision.InformacionInsuficiente }, motivos);
        }
        finally { await LimpiarAsync(cs, l); }
    }

    [Fact]
    public async Task Aprobar_SinScore_NoAprobable_SinEscrituras()
    {
        if (!TryConnString(out var cs)) return;
        var l = new Limpieza();
        try
        {
            var o = new SeedOpts { Score = null, EstadoScore = CarteraEstadoScore.SinDato };
            var s = await SembrarAsync(cs, o, l);
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoResolucionManual.NoAprobable,
                    (await Store(c).ResolverAsync(s.IdSolicitud, CarteraDecisionCrediticia.Aprobada,
                        CarteraRevisionManualMotivos.AprobacionPolitica, null, s.IdAdmin)).Resultado);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal(CarteraSolicitudCupoEstados.PendienteRevisionManual, sol.EstadoSolicitud);
            Assert.False(await v.CarteraSolicitudCupoRevisiones.AnyAsync(x => x.IdSolicitud == s.IdSolicitud));
            Assert.False(await v.CarteraCuposOrdinarios.AnyAsync(x => x.IdUsuario == s.IdUsuario));
        }
        finally { await LimpiarAsync(cs, l); }
    }

    [Fact]
    public async Task Rechazar_SinScore_Permitido_YDobleResolucionBloqueada()
    {
        if (!TryConnString(out var cs)) return;
        var l = new Limpieza();
        try
        {
            var o = new SeedOpts { Score = null, EstadoScore = CarteraEstadoScore.SinDato };
            var s = await SembrarAsync(cs, o, l);
            var motivo = CarteraRevisionManualMotivos.RechazoInformacionInsuficiente;
            await using (var c = NuevoContexto(cs))
            {
                var r1 = await Store(c).ResolverAsync(s.IdSolicitud, CarteraDecisionCrediticia.Rechazada, motivo, null, s.IdAdmin);
                Assert.Equal(ResultadoResolucionManual.Resuelta, r1.Resultado);
                Assert.Equal(CarteraSolicitudCupoEstados.Rechazada, r1.EstadoSolicitud);
            }
            await using (var c = NuevoContexto(cs))
            {
                var r2 = await Store(c).ResolverAsync(s.IdSolicitud, CarteraDecisionCrediticia.Rechazada, motivo, null, s.IdAdmin);
                Assert.Equal(ResultadoResolucionManual.YaResuelta, r2.Resultado);
            }

            await using var v = NuevoContexto(cs);
            var revs = await v.CarteraSolicitudCupoRevisiones.AsNoTracking()
                .Where(x => x.IdSolicitud == s.IdSolicitud).ToListAsync();
            Assert.Single(revs);
            Assert.Null(revs[0].MontoAprobadoManual);
            Assert.Equal(CarteraRevisionManualMotivos.ReglaRechazoManual, revs[0].ReglaAplicada);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal(0m, sol.MontoAprobado);
            Assert.Equal(motivo, sol.CodigoMotivoDecision);
        }
        finally { await LimpiarAsync(cs, l); }
    }

    [Fact]
    public async Task EstadoNoPendiente_NoElegible()
    {
        if (!TryConnString(out var cs)) return;
        var l = new Limpieza();
        try
        {
            var o = new SeedOpts
            {
                Estado = CarteraSolicitudCupoEstados.EnEvaluacion,
                Decision = CarteraDecisionCrediticia.Pendiente,
            };
            var s = await SembrarAsync(cs, o, l);
            await using var c = NuevoContexto(cs);
            var r = await Store(c).ResolverAsync(s.IdSolicitud, CarteraDecisionCrediticia.Rechazada,
                CarteraRevisionManualMotivos.RechazoRiesgo, null, s.IdAdmin);
            Assert.Equal(ResultadoResolucionManual.NoElegible, r.Resultado);
        }
        finally { await LimpiarAsync(cs, l); }
    }

    [Theory]
    [InlineData("APROBAR", "APROBACION_MANUAL_POLITICA")]
    [InlineData("APROBADA", "RECHAZO_MANUAL_RIESGO")]
    [InlineData("RECHAZADA", "APROBACION_MANUAL_POLITICA")]
    [InlineData("RECHAZADA", "")]
    public async Task DatosInvalidos_NoTocaLaBase(string decision, string motivo)
    {
        // Validacion previa a la transaccion: no requiere SQL.
        var r = await new CarteraRevisionManualStore(null!, null!)
            .ResolverAsync(1, decision, motivo, null, 5);
        Assert.Equal(ResultadoResolucionManual.DatosInvalidos, r.Resultado);
    }

    [Fact]
    public void Request_ConMonto_EsRechazado()
    {
        var json = "{\"decision\":\"APROBADA\",\"motivoCodigo\":\"APROBACION_MANUAL_POLITICA\",\"montoAprobado\":9999999}";
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ResolverRevisionManualRequest>(json, opts));
    }

    [Fact]
    public async Task Bandeja_Y_Detalle_DocumentoEnmascarado_ConPropuesta()
    {
        if (!TryConnString(out var cs)) return;
        var l = new Limpieza();
        try
        {
            var s = await SembrarAsync(cs, new SeedOpts(), l);
            await using var c = NuevoContexto(cs);
            var store = Store(c);

            var item = Assert.Single(await store.ListarPendientesAsync(),
                i => i.IdSolicitud == s.IdSolicitud);
            Assert.StartsWith("*", item.DocumentoEnmascarado);

            var d = await store.ObtenerDetalleAsync(s.IdSolicitud);
            Assert.NotNull(d);
            Assert.Equal(new[] { CarteraMotivoDecision.InformacionInsuficiente }, d!.MotivosAutomaticos);
            Assert.Equal(5, d.Comportamiento.Count);
            Assert.True(d.Propuesta.PermiteAprobar);
            Assert.Equal(MontoCasoA(), d.Propuesta.MontoPropuesto);
            Assert.Null(d.Resolucion);
        }
        finally { await LimpiarAsync(cs, l); }
    }

    // ════════════════════ infraestructura de test ════════════════════════

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de XPAY-702.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Limpieza l)
    {
        await using var ctx = NuevoContexto(cs);
        var db = ctx.Database;
        foreach (var id in l.Usuarios)
        {
            await db.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_revisiones WHERE id_solicitud IN (SELECT id_solicitud FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id})");
            await db.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_motivos_decision WHERE id_solicitud IN (SELECT id_solicitud FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id})");
            await db.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud IN (SELECT id_solicitud FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id})");
            await db.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id}");
            await db.ExecuteSqlAsync($"DELETE FROM dbo.cartera_cupos_ordinarios WHERE id_usuario = {id}");
        }
        foreach (var id in l.Wallets)
            await db.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
        foreach (var id in l.Usuarios)
            await db.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
        foreach (var id in l.Personas)
            await db.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
    }
}
