using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// M2.4c / TX2 — materialización durable DORMIDA del cupo de Cartera Ordinaria.
//
// Integración SQL de CarteraMaterializacionCupoStore.MaterializarCupoAsync
// (ICarteraMaterializacionCupo) contra el SQL Server efímero del pipeline.
// SIN red, SIN proveedor, SIN token, SIN credenciales, SIN cédulas. El
// primitivo NO tiene caller de runtime; estos tests lo instancian
// explícitamente. Guard fail-closed idéntico a M2.3b2/b3/M2.4a: local sin
// `ConnectionStrings__XpayConnection` → early-return (PASS, no SKIP); en CI
// sin la variable → FALLA (no falso verde). Reutiliza la colección
// SqlIntegration definida en CarteraConsultaRiesgoConcurrencyTests.
// ══════════════════════════════════════════════════════════════════════════

[Collection("SqlIntegration")]
public sealed class CarteraMaterializacionCupoTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const decimal MontoDefault = 2_000_000m;

    // ── TEST A — usuario sin cupo, solicitud elegible ────────────────────
    [Fact]
    public async Task Elegible_SinCupo_CreaCupoYEnlazaYAprueba()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);

            await using var ctx = NuevoContexto(cs);
            var before = DateTime.UtcNow;
            var r = await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default);
            var after = DateTime.UtcNow;

            Assert.Equal(ResultadoMaterializacionCupo.Materializado, r);

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal("APROBADA", sol.EstadoSolicitud);
            Assert.NotNull(sol.IdCupoOrdinario);
            Assert.NotNull(sol.FechaMaterializacionCupo);
            Assert.True(sol.FechaMaterializacionCupo >= before.AddSeconds(-2));
            Assert.True(sol.FechaMaterializacionCupo <= after.AddSeconds(2));

            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == sol.IdCupoOrdinario!.Value);
            Assert.Equal(s.IdUsuario, cupo.IdUsuario);
            Assert.Equal(s.IdWallet, cupo.IdWallet);
            Assert.Equal(MontoDefault, cupo.CupoAprobado);
            Assert.Equal(0m, cupo.CupoUsado);
            Assert.Equal("ACTIVO", cupo.Estado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST B — segunda llamada idempotente ────────────────────────────
    [Fact]
    public async Task SegundaLlamada_YaMaterializado_SinCambios()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);

            await using (var c1 = NuevoContexto(cs))
                Assert.Equal(ResultadoMaterializacionCupo.Materializado,
                    await new CarteraMaterializacionCupoStore(c1).MaterializarCupoAsync(s.IdSolicitud, default));

            DateTime tsMat; long idCupo; decimal cupoAprob;
            await using (var q = NuevoContexto(cs))
            {
                var sol0 = await q.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
                tsMat = sol0.FechaMaterializacionCupo!.Value;
                idCupo = sol0.IdCupoOrdinario!.Value;
                cupoAprob = (await q.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo)).CupoAprobado;
            }

            await using (var c2 = NuevoContexto(cs))
                Assert.Equal(ResultadoMaterializacionCupo.YaMaterializado,
                    await new CarteraMaterializacionCupoStore(c2).MaterializarCupoAsync(s.IdSolicitud, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal(tsMat, sol.FechaMaterializacionCupo);
            Assert.Equal(idCupo, sol.IdCupoOrdinario);
            Assert.Equal(cupoAprob, (await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo)).CupoAprobado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST C — estado distinto de APROBADA_PENDIENTE_CUPO ─────────────
    [Theory]
    [InlineData("RECIBIDA")]
    [InlineData("EN_EVALUACION")]
    [InlineData("APROBADA")]
    public async Task EstadoNoPendienteCupo_NoElegible_NadaEscrito(string estado)
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { EstadoSolicitud = estado }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default);
            Assert.Equal(ResultadoMaterializacionCupo.NoElegible, r);

            await AssertNadaMaterializadoAsync(cs, s.IdSolicitud, s.IdUsuario, estado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST D — decision_crediticia != APROBADA ────────────────────────
    [Fact]
    public async Task PendienteCupo_DecisionNoAprobada_Invariante_RollbackDurable()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { DecisionCrediticia = "PENDIENTE" }, creados);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraMaterializacionInvarianteException>(
                () => new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await AssertNadaMaterializadoAsync(cs, s.IdSolicitud, s.IdUsuario, "APROBADA_PENDIENTE_CUPO");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST E — monto_aprobado NULL ───────────────────────────────────
    [Fact]
    public async Task MontoAprobadoNull_Invariante_RollbackDurable()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = null }, creados);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraMaterializacionInvarianteException>(
                () => new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await AssertNadaMaterializadoAsync(cs, s.IdSolicitud, s.IdUsuario, "APROBADA_PENDIENTE_CUPO");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST F — monto_aprobado <= 0 ───────────────────────────────────
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task MontoAprobadoNoPositivo_Invariante_RollbackDurable(int monto)
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = monto }, creados);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraMaterializacionInvarianteException>(
                () => new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await AssertNadaMaterializadoAsync(cs, s.IdSolicitud, s.IdUsuario, "APROBADA_PENDIENTE_CUPO");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST G — id_cupo_ordinario set + fecha NULL → invariante ────────
    [Fact]
    public async Task MarcaParcial_IdSinFecha_Invariante()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);
            // cupo propio del usuario para que la FK acepte el enlace
            var idCupo = await ctxSeed.SembrarCupoAsync(s.IdUsuario, s.IdWallet, 1_000_000m, 0m, "ACTIVO", creados);
            await ctxSeed.SetSolicitudMarcaAsync(s.IdSolicitud, idCupo, fechaMat: null);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraMaterializacionInvarianteException>(
                () => new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST H — id_cupo_ordinario apunta a cupo de otro usuario ────────
    [Fact]
    public async Task MarcaCorrupta_CupoDeOtroUsuario_Invariante()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);
            var otro = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);
            var idCupoOtro = await ctxSeed.SembrarCupoAsync(otro.IdUsuario, otro.IdWallet, 1_000_000m, 0m, "ACTIVO", creados);
            await ctxSeed.SetSolicitudMarcaAsync(s.IdSolicitud, idCupoOtro, fechaMat: DateTime.UtcNow);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraMaterializacionInvarianteException>(
                () => new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST I — cupo ACTIVO existente, nuevo monto MAYOR → aumenta ─────
    [Fact]
    public async Task CupoActivoExistente_MontoMayor_Aumenta_MismoIdCupo()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = 3_000_000m }, creados);
            var idCupo = await ctxSeed.SembrarCupoAsync(s.IdUsuario, s.IdWallet, 2_000_000m, 0m, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoMaterializacionCupo.Materializado,
                await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await using var v = NuevoContexto(cs);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo);
            Assert.Equal(3_000_000m, cupo.CupoAprobado);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal(idCupo, sol.IdCupoOrdinario);
            Assert.Equal("APROBADA", sol.EstadoSolicitud);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST J — cupo ACTIVO existente, nuevo monto MENOR → no reduce ───
    [Fact]
    public async Task CupoActivoExistente_MontoMenor_NoReduce_MismoIdCupo()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = 1_500_000m }, creados);
            var idCupo = await ctxSeed.SembrarCupoAsync(s.IdUsuario, s.IdWallet, 4_000_000m, 0m, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoMaterializacionCupo.Materializado,
                await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await using var v = NuevoContexto(cs);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo);
            Assert.Equal(4_000_000m, cupo.CupoAprobado);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal(idCupo, sol.IdCupoOrdinario);
            Assert.Equal("APROBADA", sol.EstadoSolicitud);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST K — cupo_usado > 0 se preserva exactamente ────────────────
    [Fact]
    public async Task CupoActivoExistente_CupoUsadoPreservado()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = 3_000_000m }, creados);
            var idCupo = await ctxSeed.SembrarCupoAsync(s.IdUsuario, s.IdWallet, 2_000_000m, 750_000m, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoMaterializacionCupo.Materializado,
                await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await using var v = NuevoContexto(cs);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo);
            Assert.Equal(750_000m, cupo.CupoUsado);
            Assert.Equal(3_000_000m, cupo.CupoAprobado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST L/M — cupo SUSPENDIDO / CANCELADO → CupoNoActivo ───────────
    [Theory]
    [InlineData("SUSPENDIDO")]
    [InlineData("CANCELADO")]
    public async Task CupoNoActivo_NoReactiva_SolicitudYCupoIntactos(string estadoCupo)
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = 3_000_000m }, creados);
            var idCupo = await ctxSeed.SembrarCupoAsync(s.IdUsuario, s.IdWallet, 2_000_000m, 100_000m, estadoCupo, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default);
            Assert.Equal(ResultadoMaterializacionCupo.CupoNoActivo, r);

            await using var v = NuevoContexto(cs);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo);
            Assert.Equal(estadoCupo, cupo.Estado);
            Assert.Equal(2_000_000m, cupo.CupoAprobado);
            Assert.Equal(100_000m, cupo.CupoUsado);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal("APROBADA_PENDIENTE_CUPO", sol.EstadoSolicitud);
            Assert.Null(sol.IdCupoOrdinario);
            Assert.Null(sol.FechaMaterializacionCupo);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST N — misma solicitud concurrente ───────────────────────────
    [Fact]
    public async Task MismaSolicitudConcurrente_ExactamenteUnMaterializado()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);

            await using var c1 = NuevoContexto(cs);
            await using var c2 = NuevoContexto(cs);
            var res = await Task.WhenAll(
                new CarteraMaterializacionCupoStore(c1).MaterializarCupoAsync(s.IdSolicitud, default),
                new CarteraMaterializacionCupoStore(c2).MaterializarCupoAsync(s.IdSolicitud, default));

            Assert.Equal(1, res.Count(x => x == ResultadoMaterializacionCupo.Materializado));
            Assert.Equal(1, res.Count(x => x == ResultadoMaterializacionCupo.YaMaterializado));

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.CarteraCuposOrdinarios.AsNoTracking().CountAsync(c => c.IdUsuario == s.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST O — segunda aprobación secuencial del mismo usuario reutiliza el cupo existente ──
    // La solicitud A ya fue materializada (APROBADA, enlazada al cupo X). Una
    // solicitud B nueva del mismo usuario en APROBADA_PENDIENTE_CUPO es legal
    // porque A salió del conjunto del índice UNIQUE filtrado de solicitudes
    // activas (ux_cartera_solicitudes_cupo_usuario_activa). Materializar B debe
    // reutilizar exactamente X, aplicar MAX y preservar cupo_usado.
    [Fact]
    public async Task SegundaAprobacionSecuencial_ReutilizaCupoExistente_YCrece()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var baseS = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = 2_000_000m }, creados);
            var (idCupo, solB) = await ctxSeed.SembrarSegundaAprobacionSecuencialAsync(
                baseS, montoCupoExistente: 2_000_000m, cupoUsadoExistente: 400_000m, montoNuevoB: 3_000_000m, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(solB, default);
            Assert.Equal(ResultadoMaterializacionCupo.Materializado, r);

            await using var v = NuevoContexto(cs);
            var cupos = await v.CarteraCuposOrdinarios.AsNoTracking().Where(c => c.IdUsuario == baseS.IdUsuario).ToListAsync();
            Assert.Single(cupos);
            Assert.Equal(idCupo, cupos[0].IdCupo);
            Assert.Equal(3_000_000m, cupos[0].CupoAprobado);
            Assert.Equal(400_000m, cupos[0].CupoUsado);

            var solA = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == baseS.IdSolicitud);
            var solBrow = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == solB);
            Assert.Equal("APROBADA", solA.EstadoSolicitud);
            Assert.Equal(idCupo, solA.IdCupoOrdinario);
            Assert.Equal("APROBADA", solBrow.EstadoSolicitud);
            Assert.Equal(idCupo, solBrow.IdCupoOrdinario);
            Assert.NotNull(solBrow.FechaMaterializacionCupo);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST P — dos usuarios distintos concurrentes ───────────────────
    [Fact]
    public async Task DosUsuariosDistintos_AmbosMaterializado_CuposIndependientes()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var a = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);
            var b = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);

            await using var c1 = NuevoContexto(cs);
            await using var c2 = NuevoContexto(cs);
            var res = await Task.WhenAll(
                new CarteraMaterializacionCupoStore(c1).MaterializarCupoAsync(a.IdSolicitud, default),
                new CarteraMaterializacionCupoStore(c2).MaterializarCupoAsync(b.IdSolicitud, default));

            Assert.All(res, x => Assert.Equal(ResultadoMaterializacionCupo.Materializado, x));

            await using var v = NuevoContexto(cs);
            var solA = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == a.IdSolicitud);
            var solB = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == b.IdSolicitud);
            Assert.NotNull(solA.IdCupoOrdinario);
            Assert.NotNull(solB.IdCupoOrdinario);
            Assert.NotEqual(solA.IdCupoOrdinario, solB.IdCupoOrdinario);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST Q — escrituras prohibidas intactas ────────────────────────
    [Fact]
    public async Task Materializa_NoTocaDecisionNiRiesgoNiCupoUsado()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = 3_000_000m }, creados);
            var idCupo = await ctxSeed.SembrarCupoAsync(s.IdUsuario, s.IdWallet, 2_000_000m, 500_000m, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoMaterializacionCupo.Materializado,
                await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal("APROBADA", sol.DecisionCrediticia);
            Assert.Equal(3_000_000m, sol.MontoAprobado);
            Assert.Null(sol.FechaDecision);
            Assert.Null(sol.CodigoMotivoDecision);
            Assert.Null(sol.ScoreObservado);
            Assert.Null(sol.EstadoScore);
            Assert.Null(sol.ConInformacionObservado);
            Assert.Null(sol.ViabilidadObservada);
            Assert.Null(sol.RatingRecaudosObservado);
            Assert.Null(sol.MontoSugeridoObservado);
            Assert.Null(sol.AlertasCountObservado);
            Assert.Null(sol.EdadCalculadaAlMomento);
            Assert.Equal(500_000m, (await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo)).CupoUsado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST R — rollback durable ante invariante ──────────────────────
    [Fact]
    public async Task Invariante_RollbackDurable_ContextoFresco()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { DecisionCrediticia = "RECHAZADA" }, creados);
            var idCupo = await ctxSeed.SembrarCupoAsync(s.IdUsuario, s.IdWallet, 1_000_000m, 200_000m, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<CarteraMaterializacionInvarianteException>(
                () => new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await using var v = NuevoContexto(cs);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal("APROBADA_PENDIENTE_CUPO", sol.EstadoSolicitud);
            Assert.Null(sol.IdCupoOrdinario);
            Assert.Null(sol.FechaMaterializacionCupo);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo);
            Assert.Equal(1_000_000m, cupo.CupoAprobado);
            Assert.Equal(200_000m, cupo.CupoUsado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST S — aislamiento multi-producto (libranza intacta) ─────────
    [Fact]
    public async Task Materializa_NoTocaLibranzaAnticipo()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts(), creados);
            var anticipoAntes = await ctxSeed.SembrarLibranzaAnticipoAsync(s.IdUsuario, s.IdWallet, creados);

            var movLedgerAntes = await ScalarCountAsync(cs, "SELECT COUNT(*) AS Value FROM ledger_movimientos");
            var txLedgerAntes = await ScalarCountAsync(cs, "SELECT COUNT(*) AS Value FROM ledger_transacciones");
            var movWalletAntes = await ScalarCountAsync(cs, "SELECT COUNT(*) AS Value FROM wallet_movimientos");

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoMaterializacionCupo.Materializado,
                await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await using var v = NuevoContexto(cs);
            var anticipoDespues = await v.LibranzaAnticipos.AsNoTracking().SingleAsync(a => a.IdAnticipo == anticipoAntes.IdAnticipo);
            Assert.Equal(anticipoAntes.Estado, anticipoDespues.Estado);
            Assert.Equal(anticipoAntes.ValorSolicitado, anticipoDespues.ValorSolicitado);
            Assert.Equal(anticipoAntes.ValorNetoDesembolsado, anticipoDespues.ValorNetoDesembolsado);
            Assert.Equal(anticipoAntes.ValorTotalACobrar, anticipoDespues.ValorTotalACobrar);
            Assert.Equal(anticipoAntes.IdTransaccionLedgerDesembolso, anticipoDespues.IdTransaccionLedgerDesembolso);
            Assert.Equal(anticipoAntes.IdTransaccionLedgerPago, anticipoDespues.IdTransaccionLedgerPago);
            Assert.Equal(anticipoAntes.UpdatedAt, anticipoDespues.UpdatedAt);

            Assert.Equal(movLedgerAntes, await ScalarCountAsync(cs, "SELECT COUNT(*) AS Value FROM ledger_movimientos"));
            Assert.Equal(txLedgerAntes, await ScalarCountAsync(cs, "SELECT COUNT(*) AS Value FROM ledger_transacciones"));
            Assert.Equal(movWalletAntes, await ScalarCountAsync(cs, "SELECT COUNT(*) AS Value FROM wallet_movimientos"));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST T — sin wallet PERSONA ACTIVA en rama CREATE ──────────────
    [Fact]
    public async Task SinWalletPersonaActiva_NoElegible_NoCreaCupoNiWallet()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { WalletEstado = "BLOQUEADA" }, creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default);
            Assert.Equal(ResultadoMaterializacionCupo.NoElegible, r);

            await using var v = NuevoContexto(cs);
            Assert.Equal(0, await v.CarteraCuposOrdinarios.AsNoTracking().CountAsync(c => c.IdUsuario == s.IdUsuario));
            Assert.Equal(1, await v.Wallets.AsNoTracking().CountAsync(w => w.IdPersona == s.IdPersona));
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal("APROBADA_PENDIENTE_CUPO", sol.EstadoSolicitud);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── TEST U — cupo ACTIVO, monto == cupo_aprobado ───────────────────
    [Fact]
    public async Task CupoActivoExistente_MontoIgual_Materializa_MontoIntacto_UpdatedAtActualizado()
    {
        if (!TryConnString(out var cs)) return;
        var ctxSeed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var s = await ctxSeed.SembrarSolicitudAsync(new SiembraOpts { MontoAprobado = 2_500_000m }, creados);
            var idCupo = await ctxSeed.SembrarCupoAsync(s.IdUsuario, s.IdWallet, 2_500_000m, 0m, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(ResultadoMaterializacionCupo.Materializado,
                await new CarteraMaterializacionCupoStore(ctx).MaterializarCupoAsync(s.IdSolicitud, default));

            await using var v = NuevoContexto(cs);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo);
            Assert.Equal(2_500_000m, cupo.CupoAprobado);
            Assert.NotNull(cupo.UpdatedAt);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == s.IdSolicitud);
            Assert.Equal(idCupo, sol.IdCupoOrdinario);
            Assert.Equal("APROBADA", sol.EstadoSolicitud);
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
            $"{EnvConnString} es obligatoria en CI para las pruebas SQL de materialización de M2.4c.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Conteo escalar por SQL literal (sin parámetros, nombres de tabla fijos).
    // Se pasa un `string` (no un literal interpolado) → sin EF1002.
    private static async Task<int> ScalarCountAsync(string cs, string fullSqlWithValueAlias)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Database.SqlQueryRaw<int>(fullSqlWithValueAlias).SingleAsync();
    }

    // "Nada materializado" = ningún enlace de cupo, ninguna fecha de
    // materialización, ningún cupo creado, y el estado_solicitud QUEDA EXACTAMENTE
    // como se sembró (no se asume "≠ APROBADA": la solicitud pudo sembrarse ya
    // en APROBADA para probar el guard de estado).
    private static async Task AssertNadaMaterializadoAsync(string cs, long idSolicitud, long idUsuario, string estadoEsperado)
    {
        await using var v = NuevoContexto(cs);
        var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == idSolicitud);
        Assert.Null(sol.IdCupoOrdinario);
        Assert.Null(sol.FechaMaterializacionCupo);
        Assert.Equal(estadoEsperado, sol.EstadoSolicitud);
        Assert.Equal(0, await v.CarteraCuposOrdinarios.AsNoTracking().CountAsync(c => c.IdUsuario == idUsuario));
    }

    private sealed class SiembraOpts
    {
        public string EstadoSolicitud { get; set; } = "APROBADA_PENDIENTE_CUPO";
        public string DecisionCrediticia { get; set; } = "APROBADA";
        public decimal? MontoAprobado { get; set; } = MontoDefault;
        public string WalletEstado { get; set; } = "ACTIVA";
    }

    private sealed class SembradoSolicitud
    {
        public long IdSolicitud { get; init; }
        public long IdUsuario { get; init; }
        public long IdPersona { get; init; }
        public long IdWallet { get; init; }
        public long IdPolitica { get; init; }
        public long IdUnidad { get; init; }
    }

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets { get; } = new();
        public List<long> Solicitudes { get; } = new();
        public List<long> Cupos { get; } = new();
        public List<long> Anticipos { get; } = new();
        public List<long> Empleados { get; } = new();
        public List<long> Convenios { get; } = new();
    }

    private sealed class SeedContext(string cs)
    {
        private long? _idUnidad;
        private long? _idPolitica;

        private async Task<long> IdUnidadAsync()
        {
            if (_idUnidad is not null) return _idUnidad.Value;
            await using var ctx = NuevoContexto(cs);
            _idUnidad = await ctx.Database
                .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
                .SingleAsync();
            return _idUnidad.Value;
        }

        private async Task<long> IdPoliticaAsync()
        {
            if (_idPolitica is not null) return _idPolitica.Value;
            await using var ctx = NuevoContexto(cs);
            _idPolitica = await ctx.CarteraPoliticasCredito.AsNoTracking()
                .Where(p => p.Estado == "ACTIVO")
                .OrderBy(p => p.IdPolitica)
                .Select(p => p.IdPolitica)
                .FirstAsync();
            return _idPolitica.Value;
        }

        public async Task<SembradoSolicitud> SembrarSolicitudAsync(SiembraOpts o, Sembrados creados)
        {
            var idUnidad = await IdUnidadAsync();
            var idPolitica = await IdPoliticaAsync();
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..12];
            var doc = $"76{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}";

            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad,
                TipoDocumento = "CC",
                NumeroDocumento = doc,
                PrimerNombre = "MatTest",
                PrimerApellido = "Sintetico",
                Celular = "3000000000",
                Pais = "Colombia",
                Estado = "ACTIVA",
                FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona,
                NombreUsuario = $"mat_test_{sufijo}",
                PasswordHash = "x",
                Estado = "ACTIVO",
                FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            creados.Usuarios.Add(usuario.IdUsuario);

            var wallet = new Wallet
            {
                IdUnidadNegocio = idUnidad,
                TipoWallet = "PERSONA",
                IdPersona = persona.IdPersona,
                NombreWallet = $"w_{sufijo}",
                Estado = o.WalletEstado,
                FechaCreacion = ahora,
            };
            ctx.Wallets.Add(wallet);
            await ctx.SaveChangesAsync();
            creados.Wallets.Add(wallet.IdWallet);
            ctx.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = ahora });
            await ctx.SaveChangesAsync();

            var solicitud = new CarteraSolicitudCupo
            {
                IdUsuario = usuario.IdUsuario,
                IdPersona = persona.IdPersona,
                MontoSolicitado = 500_000m,
                EstadoSolicitud = o.EstadoSolicitud,
                DecisionCrediticia = o.DecisionCrediticia,
                MontoAprobado = o.MontoAprobado,
                IdPoliticaAplicada = idPolitica,
                CupoMinimoAplicado = 0m,
                CupoMaximoAplicado = 10_000_000m,
                EdadMinimaAplicada = 18,
                EdadMaximaAplicada = 99,
                NumeroIntento = 1,
                CorrelationId = $"mat-sol-{sufijo}",
                FechaSolicitud = ahora,
                FechaActualizacion = ahora,
            };
            ctx.CarteraSolicitudesCupo.Add(solicitud);
            await ctx.SaveChangesAsync();
            creados.Solicitudes.Add(solicitud.IdSolicitud);

            return new SembradoSolicitud
            {
                IdSolicitud = solicitud.IdSolicitud,
                IdUsuario = usuario.IdUsuario,
                IdPersona = persona.IdPersona,
                IdWallet = wallet.IdWallet,
                IdPolitica = idPolitica,
                IdUnidad = idUnidad,
            };
        }

        // TEST O — segunda aprobación SECUENCIAL del mismo usuario. La solicitud
        // A (baseS) se transiciona a APROBADA + enlazada a un cupo X ya
        // materializado; luego se inserta una solicitud B nueva del mismo
        // usuario en APROBADA_PENDIENTE_CUPO (legal porque A ya no participa en
        // el índice UNIQUE filtrado de solicitudes activas). Devuelve (X, B).
        public async Task<(long idCupo, long idSolicitudB)> SembrarSegundaAprobacionSecuencialAsync(
            SembradoSolicitud baseS, decimal montoCupoExistente, decimal cupoUsadoExistente, decimal montoNuevoB, Sembrados creados)
        {
            var idCupo = await SembrarCupoAsync(
                baseS.IdUsuario, baseS.IdWallet, montoCupoExistente, cupoUsadoExistente, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);

            var solA = await ctx.CarteraSolicitudesCupo.SingleAsync(s => s.IdSolicitud == baseS.IdSolicitud);
            solA.EstadoSolicitud = "APROBADA";
            solA.IdCupoOrdinario = idCupo;
            solA.FechaMaterializacionCupo = DateTime.UtcNow.AddMinutes(-5);
            await ctx.SaveChangesAsync();

            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..12];
            var solicitudB = new CarteraSolicitudCupo
            {
                IdUsuario = baseS.IdUsuario,
                IdPersona = baseS.IdPersona,
                MontoSolicitado = 500_000m,
                EstadoSolicitud = "APROBADA_PENDIENTE_CUPO",
                DecisionCrediticia = "APROBADA",
                MontoAprobado = montoNuevoB,
                IdPoliticaAplicada = baseS.IdPolitica,
                CupoMinimoAplicado = 0m,
                CupoMaximoAplicado = 10_000_000m,
                EdadMinimaAplicada = 18,
                EdadMaximaAplicada = 99,
                NumeroIntento = 1,
                CorrelationId = $"mat-sol-{sufijo}",
                FechaSolicitud = ahora,
                FechaActualizacion = ahora,
            };
            ctx.CarteraSolicitudesCupo.Add(solicitudB);
            await ctx.SaveChangesAsync();
            creados.Solicitudes.Add(solicitudB.IdSolicitud);
            return (idCupo, solicitudB.IdSolicitud);
        }

        public async Task<long> SembrarCupoAsync(long idUsuario, long idWallet, decimal aprobado, decimal usado, string estado, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var cupo = new CarteraCupoOrdinario
            {
                IdUsuario = idUsuario,
                IdWallet = idWallet,
                CupoAprobado = aprobado,
                CupoUsado = usado,
                Estado = estado,
                FechaAprobacion = ahora,
                CreatedAt = ahora,
            };
            ctx.CarteraCuposOrdinarios.Add(cupo);
            await ctx.SaveChangesAsync();
            creados.Cupos.Add(cupo.IdCupo);
            return cupo.IdCupo;
        }

        public async Task SetSolicitudMarcaAsync(long idSolicitud, long idCupo, DateTime? fechaMat)
        {
            await using var ctx = NuevoContexto(cs);
            var sol = await ctx.CarteraSolicitudesCupo.SingleAsync(s => s.IdSolicitud == idSolicitud);
            sol.IdCupoOrdinario = idCupo;
            sol.FechaMaterializacionCupo = fechaMat;
            await ctx.SaveChangesAsync();
        }

        public async Task<LibranzaAnticipo> SembrarLibranzaAnticipoAsync(long idUsuario, long idWallet, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..12];

            var convenio = new LibranzaEmpresaConvenio
            {
                NombreEmpresa = $"Conv {sufijo}",
                Nit = $"9{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}",
                Estado = "ACTIVO",
                PeriodicidadPago = "MENSUAL",
                PorcentajeMaximoCupo = 30m,
                FechaInicio = ahora,
                CreatedAt = ahora,
            };
            ctx.LibranzaEmpresasConvenio.Add(convenio);
            await ctx.SaveChangesAsync();
            creados.Convenios.Add(convenio.IdConvenio);

            var empleado = new LibranzaEmpleado
            {
                IdConvenio = convenio.IdConvenio,
                TipoDocumento = "CC",
                NumeroDocumento = $"88{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                Nombres = "EmpTest",
                SalarioMensual = 3_000_000m,
                PeriodicidadPago = "MENSUAL",
                Estado = "ACTIVO",
                CupoPreliminar = 900_000m,
                OrigenCarga = "MANUAL",
                CreatedAt = ahora,
            };
            ctx.LibranzaEmpleados.Add(empleado);
            await ctx.SaveChangesAsync();
            creados.Empleados.Add(empleado.IdEmpleado);

            var anticipo = new LibranzaAnticipo
            {
                IdConvenio = convenio.IdConvenio,
                IdEmpleado = empleado.IdEmpleado,
                IdUsuario = idUsuario,
                IdWallet = idWallet,
                FechaSolicitud = ahora,
                DiaPagoCorte = 15,
                ValorPagoProgramado = 300_000m,
                PorcentajeCupo = 30m,
                ValorCupoBase = 900_000m,
                ValorSolicitado = 300_000m,
                ValorComision = 9_000m,
                ValorIva = 1_710m,
                ValorTotalACobrar = 310_710m,
                ValorNetoDesembolsado = 300_000m,
                MomentoCobroComision = "VENCIDO",
                Estado = "DESEMBOLSADO",
                IdTransaccionLedgerDesembolso = null,
                IdTransaccionLedgerPago = null,
                CreatedAt = ahora,
            };
            ctx.LibranzaAnticipos.Add(anticipo);
            await ctx.SaveChangesAsync();
            creados.Anticipos.Add(anticipo.IdAnticipo);
            return anticipo;
        }
    }

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_solicitud = {id}");
            foreach (var id in creados.Cupos)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_cupos_ordinarios WHERE id_cupo = {id}");
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_cupos_ordinarios WHERE id_usuario = {id}");
            foreach (var id in creados.Anticipos)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_anticipos WHERE id_anticipo = {id}");
            foreach (var id in creados.Empleados)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empleados WHERE id_empleado = {id}");
            foreach (var id in creados.Convenios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empresas_convenio WHERE id_convenio = {id}");
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {id}");
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[CarteraMaterializacionCupoTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", creados.Personas)}] usuarios=[{string.Join(",", creados.Usuarios)}] " +
                $"solicitudes=[{string.Join(",", creados.Solicitudes)}] cupos=[{string.Join(",", creados.Cupos)}]");
        }
    }
}
