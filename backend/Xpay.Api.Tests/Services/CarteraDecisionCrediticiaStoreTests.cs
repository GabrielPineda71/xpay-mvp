using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// M2.4b — persistencia de la decisión crediticia DORMIDA
// (CarteraDecisionCrediticiaStore.AplicarDecisionAsync). Integración SQL
// contra el SQL Server efímero del pipeline. SIN red, SIN proveedor, SIN
// cédulas. El primitivo NO tiene caller de runtime; estos tests lo instancian
// explícitamente. Guard fail-closed idéntico a M2.4a/M2.4c: local sin
// ConnectionStrings__XpayConnection → early-return (PASS) ; en CI sin la
// variable → FALLA. Reutiliza la colección SqlIntegration.
// ══════════════════════════════════════════════════════════════════════════

[Collection("SqlIntegration")]
public sealed class CarteraDecisionCrediticiaStoreTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    private static string VentanaN(int a, int m, string comp)
    {
        var lista = new List<CarteraComportamientoVectorItemRaw>();
        var (ca, cm) = (a, m - 1);
        if (cm == 0) { cm = 12; ca -= 1; }              // M-1
        for (var i = 0; i < 6; i++)
        {
            lista.Insert(0, new CarteraComportamientoVectorItemRaw($"{ca}-{cm}", comp));
            cm -= 1;
            if (cm == 0) { cm = 12; ca -= 1; }
        }
        return JsonSerializer.Serialize(lista);
    }

    private sealed class SeedOpts
    {
        public string  EstadoSolicitud = CarteraSolicitudCupoEstados.EnEvaluacion;
        public string  DecisionCrediticia = CarteraDecisionCrediticia.Pendiente;
        public DateTime? FechaDecision;
        public bool    Consumido = true;
        public bool?   ConInformacion = true;
        public int?    Score = 850;
        public string? EstadoScore = CarteraEstadoScore.Disponible;
        public string? Viabilidad = "ALTA";
        public string? Rating = "A";
        public string? TipoDoc = "CC";
        public string? EstadoDocId = "Vigente";
        public string? EstadoDocCap = "PRESENTE";
        public string? RangoEdadId = "36-45";
        public string? RangoEdadCap = "PRESENTE";
        public string? VectorJson = VentanaN(2026, 9, "N");
        public int?    VectorCount = 6;
        public string? ConsultaAnio = "2026";
        public string? ConsultaMes = "9";
    }

    private static async Task<(long idSolicitud, long idUsuario)> SembrarAsync(string cs, SeedOpts o, List<long> personas, List<long> usuarios, List<long> solicitudes)
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
            NumeroDocumento = $"76{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
            PrimerNombre = "DecTest", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"dec_test_{sufijo}",
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        usuarios.Add(usuario.IdUsuario);

        var solicitud = new CarteraSolicitudCupo
        {
            IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona, MontoSolicitado = 500_000m,
            EstadoSolicitud = o.EstadoSolicitud, DecisionCrediticia = o.DecisionCrediticia, FechaDecision = o.FechaDecision,
            IdPoliticaAplicada = idPolitica, CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
            EdadMinimaAplicada = 18, EdadMaximaAplicada = 99, NumeroIntento = 1,
            CorrelationId = $"dec-sol-{sufijo}", FechaSolicitud = ahora, FechaActualizacion = ahora,
            ConInformacionObservado = o.ConInformacion, ScoreObservado = o.Score, EstadoScore = o.EstadoScore,
            ViabilidadObservada = o.Viabilidad, RatingRecaudosObservado = o.Rating,
            TipoDocumentoObservado = o.TipoDoc,
            EstadoDocumentoInfoDemograficaRaw = o.EstadoDocId, EstadoDocumentoCaptura = o.EstadoDocCap,
            RangoEdadInfoDemograficaRaw = o.RangoEdadId, RangoEdadCaptura = o.RangoEdadCap,
            ComportamientoVectorJson = o.VectorJson, ComportamientoVectorCount = o.VectorCount,
            ConsultaAnioRaw = o.ConsultaAnio, ConsultaMesRaw = o.ConsultaMes,
        };
        ctx.CarteraSolicitudesCupo.Add(solicitud);
        await ctx.SaveChangesAsync();
        solicitudes.Add(solicitud.IdSolicitud);

        ctx.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
        {
            IdSolicitud = solicitud.IdSolicitud, NumeroIntento = 1, IdempotencyKey = Guid.NewGuid(),
            FechaInicio = ahora.AddMinutes(-3), FechaFin = ahora.AddMinutes(-2),
            ResultadoTecnico = CarteraConsultaRiesgoResultados.Aceptada, HttpStatusObservado = 200,
            ContentStatusObservado = "202 ACCEPTED", CorrelationId = $"dec-int-{sufijo}",
            EsIntentoConResultadoUtil = true, FaseIntento = CarteraIntentoFases.Finalizado,
            ResultadoConsumidoUtc = o.Consumido ? ahora.AddMinutes(-1) : null,
        });
        await ctx.SaveChangesAsync();

        return (solicitud.IdSolicitud, usuario.IdUsuario);
    }

    // ── TEST 1 — APROBADA: padre + 0 motivos ────────────────────────────
    [Fact]
    public async Task Aprobada_PadreSinMotivos()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts(), pe, us, so);
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoAplicacionDecision.Aplicada,
                    await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.Equal(CarteraDecisionCrediticia.Aprobada, sol.DecisionCrediticia);
            Assert.Equal(CarteraSolicitudCupoEstados.AprobadaPendienteCupo, sol.EstadoSolicitud);
            Assert.True(sol.MontoAprobado > 0m);
            Assert.Null(sol.CodigoMotivoDecision);
            Assert.NotNull(sol.FechaDecision);
            Assert.Equal(0, await v.CarteraSolicitudCupoMotivosDecision.AsNoTracking().CountAsync(m => m.IdSolicitud == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 2 — RECHAZADA: padre + motivos ordenados ; primario == orden1 ──
    [Fact]
    public async Task Rechazada_MotivosOrdenados_PrimarioOrden1()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            // viabilidad BAJA (grupo 6) + rating N (grupo 7) — rechazo puro (sin edad)
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts { Viabilidad = "BAJA", Rating = "N" }, pe, us, so);
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoAplicacionDecision.Aplicada,
                    await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.Equal(CarteraDecisionCrediticia.Rechazada, sol.DecisionCrediticia);
            Assert.Equal(CarteraSolicitudCupoEstados.Rechazada, sol.EstadoSolicitud);
            Assert.Equal(0m, sol.MontoAprobado);
            Assert.Equal(CarteraMotivoDecision.ViabilidadBaja, sol.CodigoMotivoDecision);

            var motivos = await v.CarteraSolicitudCupoMotivosDecision.AsNoTracking()
                .Where(m => m.IdSolicitud == idSol).OrderBy(m => m.Orden).ToListAsync();
            Assert.Equal(new short[] { 1, 2 }, motivos.Select(m => m.Orden).ToArray());
            Assert.Equal(CarteraMotivoDecision.ViabilidadBaja, motivos[0].CodigoMotivo);
            Assert.Equal(CarteraMotivoDecision.RatingRecaudosInsuficiente, motivos[1].CodigoMotivo);
            Assert.Equal(sol.CodigoMotivoDecision, motivos[0].CodigoMotivo); // invariante padre/hija
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 2b — XPAY-190 / ACTA 001 · CASE 5 — edad 66+ → NO_DECIDIBLE /
    //    EDAD_REQUIERE_REVISION_MANUAL → PENDIENTE_REVISION_MANUAL / monto NULL ──
    [Fact]
    public async Task NoDecidible_Edad66_EdadRequiereRevisionManual()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts { RangoEdadId = "66" }, pe, us, so);
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoAplicacionDecision.Aplicada,
                    await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.Equal(CarteraDecisionCrediticia.NoDecidible, sol.DecisionCrediticia);
            Assert.Equal(CarteraSolicitudCupoEstados.PendienteRevisionManual, sol.EstadoSolicitud);
            Assert.Null(sol.MontoAprobado);
            Assert.Equal(CarteraMotivoDecision.EdadRequiereRevisionManual, sol.CodigoMotivoDecision);

            var motivos = await v.CarteraSolicitudCupoMotivosDecision.AsNoTracking()
                .Where(m => m.IdSolicitud == idSol).OrderBy(m => m.Orden).ToListAsync();
            Assert.Equal(CarteraMotivoDecision.EdadRequiereRevisionManual, motivos[0].CodigoMotivo);
            Assert.Equal(sol.CodigoMotivoDecision, motivos[0].CodigoMotivo); // invariante padre/hija
            Assert.DoesNotContain(motivos, m => m.CodigoMotivo == CarteraMotivoDecision.EdadFueraPolitica);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 3 — NO_DECIDIBLE: estado + monto NULL + motivos ────────────
    [Fact]
    public async Task NoDecidible_PendienteRevisionManual_MontoNull()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts { Viabilidad = null }, pe, us, so);
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoAplicacionDecision.Aplicada,
                    await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.Equal(CarteraDecisionCrediticia.NoDecidible, sol.DecisionCrediticia);
            Assert.Equal(CarteraSolicitudCupoEstados.PendienteRevisionManual, sol.EstadoSolicitud);
            Assert.Null(sol.MontoAprobado);
            Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, sol.CodigoMotivoDecision);
            Assert.True(await v.CarteraSolicitudCupoMotivosDecision.AsNoTracking().AnyAsync(m => m.IdSolicitud == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 4 — idempotencia: segunda aplicación → YaDecidido, sin rewrite ──
    [Fact]
    public async Task SegundaAplicacion_YaDecidido_SinRewrite()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts(), pe, us, so);
            await using (var c1 = NuevoContexto(cs))
                Assert.Equal(ResultadoAplicacionDecision.Aplicada,
                    await new CarteraDecisionCrediticiaStore(c1).AplicarDecisionAsync(idSol, default));

            DateTime fecha1;
            await using (var q = NuevoContexto(cs))
                fecha1 = (await q.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol)).FechaDecision!.Value;

            await using (var c2 = NuevoContexto(cs))
                Assert.Equal(ResultadoAplicacionDecision.YaDecidido,
                    await new CarteraDecisionCrediticiaStore(c2).AplicarDecisionAsync(idSol, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.Equal(fecha1, sol.FechaDecision);
            Assert.Equal(CarteraSolicitudCupoEstados.AprobadaPendienteCupo, sol.EstadoSolicitud);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 5 — estado incoherente (decisión sin fecha) → invariante ───
    [Fact]
    public async Task DecisionSinFecha_Invariante()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs,
                new SeedOpts { DecisionCrediticia = CarteraDecisionCrediticia.Aprobada, FechaDecision = null }, pe, us, so);

            await using var c = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraDecisionInvarianteException>(() =>
                new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.Null(sol.FechaDecision);
            Assert.Equal(0, await v.CarteraSolicitudCupoMotivosDecision.AsNoTracking().CountAsync(m => m.IdSolicitud == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 6 — no EN_EVALUACION / no consumido → NoElegible ───────────
    [Fact]
    public async Task NoEnEvaluacion_NoElegible()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts { EstadoSolicitud = CarteraSolicitudCupoEstados.Recibida }, pe, us, so);
            await using var c = NuevoContexto(cs);
            Assert.Equal(ResultadoAplicacionDecision.NoElegible,
                await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    [Fact]
    public async Task ResultadoNoConsumido_NoElegible()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts { Consumido = false }, pe, us, so);
            await using var c = NuevoContexto(cs);
            Assert.Equal(ResultadoAplicacionDecision.NoElegible,
                await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 7 — señal POSIBLE_SUPLANTACION ────────────────────────────
    [Fact]
    public async Task SenalPosibleSuplantacion_True_En_Muerte()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts { EstadoDocId = "Cancelada por muerte o fallecido" }, pe, us, so);
            await using (var c = NuevoContexto(cs))
                await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.True(sol.SenalPosibleSuplantacion);
            Assert.Equal(CarteraDecisionCrediticia.Rechazada, sol.DecisionCrediticia);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    [Fact]
    public async Task SenalPosibleSuplantacion_False_En_Vigente()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts(), pe, us, so);
            await using (var c = NuevoContexto(cs))
                await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default);
            await using var v = NuevoContexto(cs);
            Assert.False((await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol)).SenalPosibleSuplantacion);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 8 — índice activo: NO_DECIDIBLE bloquea segunda solicitud ──
    [Fact]
    public async Task PendienteRevisionManual_BloqueaSegundaSolicitud()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsuario) = await SembrarAsync(cs, new SeedOpts { Viabilidad = null }, pe, us, so);
            await using (var c = NuevoContexto(cs))
                await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default);

            await using var ctx = NuevoContexto(cs);
            var idPolitica = await ctx.CarteraPoliticasCredito.AsNoTracking()
                .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();
            var idPersona = (await ctx.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol)).IdPersona;

            ctx.CarteraSolicitudesCupo.Add(new CarteraSolicitudCupo
            {
                IdUsuario = idUsuario, IdPersona = idPersona, MontoSolicitado = 300_000m,
                EstadoSolicitud = CarteraSolicitudCupoEstados.Recibida, DecisionCrediticia = CarteraDecisionCrediticia.Pendiente,
                IdPoliticaAplicada = idPolitica, CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
                EdadMinimaAplicada = 18, EdadMaximaAplicada = 99, NumeroIntento = 1,
                CorrelationId = $"dec-sol2-{Guid.NewGuid():N}"[..40],
                FechaSolicitud = DateTime.UtcNow, FechaActualizacion = DateTime.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 9 — M2.4c devuelve NoElegible para una solicitud NO_DECIDIBLE ──
    [Fact]
    public async Task M2_4c_NoElegible_Para_NoDecidible()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts { Viabilidad = null }, pe, us, so);
            await using (var c = NuevoContexto(cs))
                await new CarteraDecisionCrediticiaStore(c).AplicarDecisionAsync(idSol, default);

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoMaterializacionCupo.NoElegible,
                await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(idSol, default));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 10 — concurrencia: dos aplicaciones simultáneas serializadas ──
    [Fact]
    public async Task MismaSolicitudConcurrente_UnaAplicaUnaYaDecidido()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _) = await SembrarAsync(cs, new SeedOpts(), pe, us, so);

            await using var c1 = NuevoContexto(cs);
            await using var c2 = NuevoContexto(cs);
            var res = await Task.WhenAll(
                new CarteraDecisionCrediticiaStore(c1).AplicarDecisionAsync(idSol, default),
                new CarteraDecisionCrediticiaStore(c2).AplicarDecisionAsync(idSol, default));

            Assert.Equal(1, res.Count(x => x == ResultadoAplicacionDecision.Aplicada));
            Assert.Equal(1, res.Count(x => x == ResultadoAplicacionDecision.YaDecidido));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.Equal(CarteraDecisionCrediticia.Aprobada, sol.DecisionCrediticia);
            Assert.Equal(CarteraSolicitudCupoEstados.AprobadaPendienteCupo, sol.EstadoSolicitud);
            // APROBADA → 0 filas de motivo (una sola aplicación efectiva).
            Assert.Equal(0, await v.CarteraSolicitudCupoMotivosDecision.AsNoTracking().CountAsync(m => m.IdSolicitud == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ════════════════════ infraestructura de test ════════════════════════

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de decisión de M2.4b.");
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
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_motivos_decision WHERE id_solicitud = {id}");
            foreach (var id in solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud = {id}");
            // Solicitudes por usuario (puede haberse creado una 2ª en el TEST 8).
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_motivos_decision WHERE id_solicitud IN (SELECT id_solicitud FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id})");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud IN (SELECT id_solicitud FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id})");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id}");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[CarteraDecisionCrediticiaStoreTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", personas)}] usuarios=[{string.Join(",", usuarios)}] solicitudes=[{string.Join(",", solicitudes)}]");
        }
    }
}
