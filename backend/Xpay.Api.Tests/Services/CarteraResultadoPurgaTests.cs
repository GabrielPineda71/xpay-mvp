using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// M2.3b3 — integración SQL de la INFRAESTRUCTURA DORMIDA de purga
// (CarteraConsultaRiesgoStore.PurgarResultadoIntentoAsync /
// ICarteraResultadoRiesgoPurga) contra el SQL Server efímero del pipeline CI.
//
// SIN red, SIN proveedor real, SIN token, SIN credenciales de proveedor, SIN
// cédulas. El primitivo NO tiene caller de runtime; estos tests lo instancian
// explícitamente — es la única forma de alcanzarlo.
//
// Local sin `ConnectionStrings__XpayConnection`: los [Fact]/[Theory] retornan
// temprano y CUENTAN COMO PASS (no SKIP). En CI la variable la fija el step
// `dotnet test` (M2.3b2); si allí faltara, el test FALLA (no falso verde).
//
// Reutiliza la colección SqlIntegration definida en
// CarteraConsultaRiesgoConcurrencyTests (no se redefine).
[Collection("SqlIntegration")]
public sealed class CarteraResultadoPurgaTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    private const string ScoreRaw       = "777";
    private const string ViabilidadRaw  = "ALTA";
    private const string RatingRaw      = "A";
    private const string MontoRaw       = "1500000";

    // ── TEST A — purga elegible ───────────────────────────────────────────
    [Fact]
    public async Task Elegible_Purga_NulaLos6Crudos_YMarcaTimestamp_SinTocarTecnicoNiDecision()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var fechaFin = DateTime.UtcNow.AddDays(-10);
            var (idSolicitud, numeroIntento) = await SembrarIntentoAsync(
                cs, idUnidad, idPolitica, CarteraIntentoFases.Finalizado, conCrudos: true, fechaFin, creados);

            await using var ctx = NuevoContexto(cs);
            var store = new CarteraConsultaRiesgoStore(ctx);

            var before = DateTime.UtcNow;
            var r = await store.PurgarResultadoIntentoAsync(idSolicitud, numeroIntento, DateTime.UtcNow, default);
            var after = DateTime.UtcNow;

            Assert.Equal(ResultadoPurgaIntento.Purgado, r);

            await using var v = NuevoContexto(cs);
            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);

            // 6 crudos NULL
            Assert.Null(it.ConInformacion);
            Assert.Null(it.ScoreRaw);
            Assert.Null(it.ViabilidadRaw);
            Assert.Null(it.RatingRecaudosRaw);
            Assert.Null(it.MontoSugeridoRaw);
            Assert.Null(it.AlertasCount);

            // marca de purga puesta y dentro del intervalo de la operación
            Assert.NotNull(it.ResultadoPurgadoUtc);
            Assert.True(it.ResultadoPurgadoUtc >= before.AddSeconds(-2), "resultado_purgado_utc anterior al inicio de la operación");
            Assert.True(it.ResultadoPurgadoUtc <= after.AddSeconds(2), "resultado_purgado_utc posterior al fin de la operación");

            // campos técnicos preservados
            Assert.Equal(CarteraIntentoFases.Finalizado, it.FaseIntento);
            Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, it.ResultadoTecnico);
            Assert.True(it.EsIntentoConResultadoUtil);
            Assert.Equal(200, it.HttpStatusObservado);
            Assert.Equal("202 ACCEPTED", it.ContentStatusObservado);
            Assert.Equal(1, it.NumeroIntento);
            Assert.Equal(fechaFin, it.FechaFin);

            // solicitud / campos de decisión sin cambio
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSolicitud);
            Assert.Equal("EN_EVALUACION", sol.EstadoSolicitud);
            Assert.Equal("PENDIENTE", sol.DecisionCrediticia);
            Assert.Null(sol.ScoreObservado);
            Assert.Null(sol.MontoSugeridoObservado);
            Assert.Null(sol.EstadoScore);
            Assert.Null(sol.ViabilidadObservada);
            Assert.Null(sol.RatingRecaudosObservado);
            Assert.Null(sol.FechaDecision);
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ── TEST B — segunda purga idempotente ────────────────────────────────
    [Fact]
    public async Task SegundaPurga_YaPurgado_TimestampNoCambia_CrudosSiguenNull()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarIntentoAsync(
                cs, idUnidad, idPolitica, CarteraIntentoFases.Finalizado, conCrudos: true, DateTime.UtcNow.AddDays(-10), creados);

            await using (var ctx1 = NuevoContexto(cs))
            {
                var r1 = await new CarteraConsultaRiesgoStore(ctx1)
                    .PurgarResultadoIntentoAsync(idSolicitud, numeroIntento, DateTime.UtcNow, default);
                Assert.Equal(ResultadoPurgaIntento.Purgado, r1);
            }

            DateTime tsPrimera;
            await using (var v1 = NuevoContexto(cs))
            {
                tsPrimera = (await v1.CarteraSolicitudCupoIntentos.AsNoTracking()
                    .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento))
                    .ResultadoPurgadoUtc!.Value;
            }

            await using (var ctx2 = NuevoContexto(cs))
            {
                var r2 = await new CarteraConsultaRiesgoStore(ctx2)
                    .PurgarResultadoIntentoAsync(idSolicitud, numeroIntento, DateTime.UtcNow, default);
                Assert.Equal(ResultadoPurgaIntento.YaPurgado, r2);
            }

            await using var v2 = NuevoContexto(cs);
            var it = await v2.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            Assert.Equal(tsPrimera, it.ResultadoPurgadoUtc);
            Assert.Null(it.ScoreRaw);
            Assert.Null(it.MontoSugeridoRaw);
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ── TEST C — guards de fase ───────────────────────────────────────────
    [Theory]
    [InlineData("PRE_CALL")]
    [InlineData("ENVIO_INCIERTO")]
    public async Task FaseNoFinalizada_NoElegible_TimestampNull_CrudosIntactos(string fase)
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarIntentoAsync(
                cs, idUnidad, idPolitica, fase, conCrudos: true, DateTime.UtcNow.AddDays(-10), creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarResultadoIntentoAsync(idSolicitud, numeroIntento, DateTime.UtcNow, default);
            Assert.Equal(ResultadoPurgaIntento.NoElegible, r);

            await using var v = NuevoContexto(cs);
            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            Assert.Null(it.ResultadoPurgadoUtc);
            Assert.Equal(ScoreRaw, it.ScoreRaw);
            Assert.Equal(MontoRaw, it.MontoSugeridoRaw);
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ── TEST D — guard de cutoff ──────────────────────────────────────────
    [Fact]
    public async Task FinalizadoPeroNoVencido_NoElegible_TimestampNull_CrudosIntactos()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            // fecha_fin de ayer, cutoff de hace 30 días → fecha_fin >= cutoff.
            var (idSolicitud, numeroIntento) = await SembrarIntentoAsync(
                cs, idUnidad, idPolitica, CarteraIntentoFases.Finalizado, conCrudos: true, DateTime.UtcNow.AddDays(-1), creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarResultadoIntentoAsync(idSolicitud, numeroIntento, DateTime.UtcNow.AddDays(-30), default);
            Assert.Equal(ResultadoPurgaIntento.NoElegible, r);

            await using var v = NuevoContexto(cs);
            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            Assert.Null(it.ResultadoPurgadoUtc);
            Assert.Equal(ScoreRaw, it.ScoreRaw);
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ── TEST E — sin ningún crudo que purgar ──────────────────────────────
    [Fact]
    public async Task FinalizadoSinCrudos_NoElegible_TimestampNull()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarIntentoAsync(
                cs, idUnidad, idPolitica, CarteraIntentoFases.Finalizado, conCrudos: false, DateTime.UtcNow.AddDays(-10), creados);

            await using var ctx = NuevoContexto(cs);
            var r = await new CarteraConsultaRiesgoStore(ctx)
                .PurgarResultadoIntentoAsync(idSolicitud, numeroIntento, DateTime.UtcNow, default);
            Assert.Equal(ResultadoPurgaIntento.NoElegible, r);

            await using var v = NuevoContexto(cs);
            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            Assert.Null(it.ResultadoPurgadoUtc);
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ── TEST F — purga concurrente sobre el mismo intento ─────────────────
    [Fact]
    public async Task PurgaConcurrenteMismoIntento_ExactamenteUnPurgado_UnicoTimestamp()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (idSolicitud, numeroIntento) = await SembrarIntentoAsync(
                cs, idUnidad, idPolitica, CarteraIntentoFases.Finalizado, conCrudos: true, DateTime.UtcNow.AddDays(-10), creados);

            await using var ctx1 = NuevoContexto(cs);
            await using var ctx2 = NuevoContexto(cs);
            var s1 = new CarteraConsultaRiesgoStore(ctx1);
            var s2 = new CarteraConsultaRiesgoStore(ctx2);

            var cutoff = DateTime.UtcNow;
            var res = await Task.WhenAll(
                s1.PurgarResultadoIntentoAsync(idSolicitud, numeroIntento, cutoff, default),
                s2.PurgarResultadoIntentoAsync(idSolicitud, numeroIntento, cutoff, default));

            Assert.Equal(1, res.Count(x => x == ResultadoPurgaIntento.Purgado));
            Assert.Equal(1, res.Count(x => x == ResultadoPurgaIntento.YaPurgado));

            await using var v = NuevoContexto(cs);
            var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                .SingleAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento);
            Assert.NotNull(it.ResultadoPurgadoUtc);
            Assert.Null(it.ScoreRaw);
            Assert.Null(it.MontoSugeridoRaw);
            Assert.Null(it.AlertasCount);
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ── TEST G — dos intentos distintos, purgas independientes ────────────
    [Fact]
    public async Task PurgaConcurrenteIntentosDistintos_AmbosPurgados()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var fechaFin = DateTime.UtcNow.AddDays(-10);
            var a = await SembrarIntentoAsync(cs, idUnidad, idPolitica, CarteraIntentoFases.Finalizado, true, fechaFin, creados);
            var b = await SembrarIntentoAsync(cs, idUnidad, idPolitica, CarteraIntentoFases.Finalizado, true, fechaFin, creados);

            await using var ctx1 = NuevoContexto(cs);
            await using var ctx2 = NuevoContexto(cs);
            var cutoff = DateTime.UtcNow;
            var res = await Task.WhenAll(
                new CarteraConsultaRiesgoStore(ctx1).PurgarResultadoIntentoAsync(a.idSolicitud, a.numeroIntento, cutoff, default),
                new CarteraConsultaRiesgoStore(ctx2).PurgarResultadoIntentoAsync(b.idSolicitud, b.numeroIntento, cutoff, default));

            Assert.All(res, x => Assert.Equal(ResultadoPurgaIntento.Purgado, x));

            await using var v = NuevoContexto(cs);
            foreach (var (idSol, num) in new[] { a, b })
            {
                var it = await v.CarteraSolicitudCupoIntentos.AsNoTracking()
                    .SingleAsync(i => i.IdSolicitud == idSol && i.NumeroIntento == num);
                Assert.NotNull(it.ResultadoPurgadoUtc);
                Assert.Null(it.ScoreRaw);
            }
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ════════════════════ infraestructura de test ════════════════════════

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;

        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi,
            $"{EnvConnString} es obligatoria en CI para las pruebas SQL de purga de M2.3b3.");
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

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Solicitudes { get; } = new();
    }

    private static async Task<(long idSolicitud, int numeroIntento)> SembrarIntentoAsync(
        string cs, long idUnidad, long idPolitica, string fase, bool conCrudos, DateTime? fechaFin, Sembrados creados)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var doc = $"76{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}"; // 9 dígitos sintéticos

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad,
            TipoDocumento   = "CC",
            NumeroDocumento = doc,
            PrimerNombre    = "PurgaTest",
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
            NombreUsuario = $"purga_test_{sufijo}",
            PasswordHash  = "x",
            Estado        = "ACTIVO",
            FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);

        var esFinalizado = string.Equals(fase, CarteraIntentoFases.Finalizado, StringComparison.Ordinal);

        var solicitud = new CarteraSolicitudCupo
        {
            IdUsuario          = usuario.IdUsuario,
            IdPersona          = persona.IdPersona,
            MontoSolicitado    = 500_000m,
            EstadoSolicitud    = esFinalizado ? "EN_EVALUACION" : CarteraSolicitudCupoEstados.Recibida,
            DecisionCrediticia = "PENDIENTE",
            IdPoliticaAplicada = idPolitica,
            CupoMinimoAplicado = 0m,
            CupoMaximoAplicado = 1_000_000m,
            EdadMinimaAplicada = 18,
            EdadMaximaAplicada = 99,
            NumeroIntento      = 1,
            CorrelationId      = $"purga-sol-{sufijo}",
            FechaSolicitud     = ahora,
            FechaActualizacion = ahora,
        };
        ctx.CarteraSolicitudesCupo.Add(solicitud);
        await ctx.SaveChangesAsync();
        creados.Solicitudes.Add(solicitud.IdSolicitud);

        ctx.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
        {
            IdSolicitud               = solicitud.IdSolicitud,
            NumeroIntento             = 1,
            IdempotencyKey            = Guid.NewGuid(),
            FechaInicio               = (fechaFin ?? ahora).AddMinutes(-1),
            FechaFin                  = esFinalizado ? fechaFin : null,
            ResultadoTecnico          = esFinalizado ? CarteraConsultaRiesgoResultados.Aceptada : null,
            HttpStatusObservado       = esFinalizado ? 200 : null,
            ContentStatusObservado    = esFinalizado ? "202 ACCEPTED" : null,
            CorrelationId             = $"purga-int-{sufijo}",
            EsIntentoConResultadoUtil = esFinalizado,
            FaseIntento               = fase,
            ConInformacion            = conCrudos ? true : null,
            ScoreRaw                  = conCrudos ? ScoreRaw : null,
            ViabilidadRaw             = conCrudos ? ViabilidadRaw : null,
            RatingRecaudosRaw         = conCrudos ? RatingRaw : null,
            MontoSugeridoRaw          = conCrudos ? MontoRaw : null,
            AlertasCount              = conCrudos ? 0 : null,
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
            // Teardown best-effort (BD de CI efímera por job) PERO con señal
            // diagnóstica segura — sólo IDs internos y el tipo de excepción,
            // sin PII ni valores crudos.
            Console.Error.WriteLine(
                $"[CarteraResultadoPurgaTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", creados.Personas)}] usuarios=[{string.Join(",", creados.Usuarios)}] " +
                $"solicitudes=[{string.Join(",", creados.Solicitudes)}]");
        }
    }
}
