using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// M2.4d — PENDIENTE_REVISION_MANUAL debe contarse como "solicitud activa" en la
// lógica C# de originación (CarteraOrdinariaService.EstadosSolicitudActivos),
// reflejando el filtro del índice UNIQUE recreado por la migración 040. Así una
// solicitud NO_DECIDIBLE de M2.4b bloquea una 2ª solicitud con un 409 limpio
// ANTES de chocar con el UNIQUE de SQL.
[Collection("SqlIntegration")]
public sealed class CarteraOrdinariaEstadosActivosTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // 15a — pura: el array incluye PENDIENTE_REVISION_MANUAL.
    [Fact]
    public void EstadosSolicitudActivos_incluye_PendienteRevisionManual()
    {
        var field = typeof(CarteraOrdinariaService).GetField(
            "EstadosSolicitudActivos", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var estados = (string[])field!.GetValue(null)!;

        Assert.Contains(CarteraSolicitudCupoEstados.PendienteRevisionManual, estados);
        // los 5 previos siguen presentes
        Assert.Contains(CarteraSolicitudCupoEstados.Recibida, estados);
        Assert.Contains(CarteraSolicitudCupoEstados.AprobadaPendienteCupo, estados);
    }

    // 15b — SQL: una solicitud en PENDIENTE_REVISION_MANUAL impide originar otra.
    [Fact]
    public async Task Solicitud_en_revision_manual_bloquea_una_segunda_originacion()
    {
        if (!TryConnString(out var cs)) return;

        var pe = new List<long>(); var us = new List<long>(); var so = new List<long>();
        try
        {
            long idUsuario;
            await using (var seed = NuevoContexto(cs))
            {
                var ahora = DateTime.UtcNow;
                var sufijo = Guid.NewGuid().ToString("N")[..12];
                var idUnidad = await seed.Database.SqlQueryRaw<long>(
                    "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
                var idPolitica = await seed.CarteraPoliticasCredito.AsNoTracking()
                    .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();

                var persona = new Persona
                {
                    IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
                    NumeroDocumento = $"78{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                    PrimerNombre = "PrmTest", PrimerApellido = "Sintetico", Celular = "3000000000",
                    Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
                };
                seed.Personas.Add(persona);
                await seed.SaveChangesAsync();
                pe.Add(persona.IdPersona);

                var usuario = new Usuario
                {
                    IdPersona = persona.IdPersona, NombreUsuario = $"prm_test_{sufijo}",
                    PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
                };
                seed.Usuarios.Add(usuario);
                await seed.SaveChangesAsync();
                us.Add(usuario.IdUsuario);
                idUsuario = usuario.IdUsuario;

                var solicitud = new CarteraSolicitudCupo
                {
                    IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona,
                    EstadoSolicitud = CarteraSolicitudCupoEstados.PendienteRevisionManual,
                    DecisionCrediticia = CarteraDecisionCrediticia.NoDecidible,
                    CodigoMotivoDecision = CarteraMotivoDecision.EdadRequiereRevisionManual,
                    FechaDecision = ahora, IdPoliticaAplicada = idPolitica,
                    CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
                    EdadMinimaAplicada = 18, EdadMaximaAplicada = 99, NumeroIntento = 1,
                    CorrelationId = $"prm-sol-{sufijo}", FechaSolicitud = ahora, FechaActualizacion = ahora,
                };
                seed.CarteraSolicitudesCupo.Add(solicitud);
                await seed.SaveChangesAsync();
                so.Add(solicitud.IdSolicitud);

                seed.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
                {
                    IdSolicitud = solicitud.IdSolicitud, NumeroIntento = 1, IdempotencyKey = Guid.NewGuid(),
                    FechaInicio = ahora.AddMinutes(-2), FechaFin = ahora.AddMinutes(-1),
                    ResultadoTecnico = CarteraConsultaRiesgoResultados.Aceptada, HttpStatusObservado = 200,
                    CorrelationId = $"prm-int-{sufijo}", EsIntentoConResultadoUtil = true,
                    FaseIntento = CarteraIntentoFases.Finalizado, ResultadoConsumidoUtc = ahora.AddMinutes(-1),
                });
                await seed.SaveChangesAsync();
            }

            await using var ctx = NuevoContexto(cs);
            var svc = new CarteraOrdinariaService(
                ctx,
                new PagoQrService(ctx, NullLogger<PagoQrService>.Instance),
                NullLogger<CarteraOrdinariaService>.Instance);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CrearSolicitudCupoAsync(idUsuario, Guid.NewGuid(), "corr-2"));

            Assert.Contains("Ya tienes una solicitud de cupo en curso", ex.Message);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // XPAY-676B — SQL: la creación sin monto persiste monto_solicitado NULL,
    // copia como aplicada la política efectiva del motor, hace replay con la
    // misma Idempotency-Key y rechaza esa key desde otro usuario.
    [Fact]
    public async Task Creacion_sin_monto_persiste_null_y_politica_del_motor()
    {
        if (!TryConnString(out var cs)) return;

        var pe = new List<long>(); var us = new List<long>(); var so = new List<long>();
        try
        {
            var ids = new List<long>();
            await using (var seed = NuevoContexto(cs))
            {
                var ahora = DateTime.UtcNow;
                var idUnidad = await seed.Database.SqlQueryRaw<long>(
                    "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
                for (var i = 0; i < 2; i++)
                {
                    var sufijo = Guid.NewGuid().ToString("N")[..12];
                    var persona = new Persona
                    {
                        IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
                        NumeroDocumento = $"79{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                        PrimerNombre = "SinMonto", PrimerApellido = "Sintetico", Celular = "3000000000",
                        Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
                    };
                    seed.Personas.Add(persona);
                    await seed.SaveChangesAsync();
                    pe.Add(persona.IdPersona);

                    var usuario = new Usuario
                    {
                        IdPersona = persona.IdPersona, NombreUsuario = $"sinmonto_test_{sufijo}",
                        PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
                    };
                    seed.Usuarios.Add(usuario);
                    await seed.SaveChangesAsync();
                    us.Add(usuario.IdUsuario);
                    ids.Add(usuario.IdUsuario);
                }
            }

            var key = Guid.NewGuid();
            await using var ctx = NuevoContexto(cs);
            var svc = new CarteraOrdinariaService(
                ctx,
                new PagoQrService(ctx, NullLogger<PagoQrService>.Instance),
                NullLogger<CarteraOrdinariaService>.Instance);

            var creada = await svc.CrearSolicitudCupoAsync(ids[0], key, "corr-676b");
            Assert.Equal(CarteraSolicitudCupoEstados.Recibida, creada.EstadoSolicitud);
            Assert.Equal(CarteraDecisionCrediticia.Pendiente, creada.DecisionCrediticia);

            var replay = await svc.CrearSolicitudCupoAsync(ids[0], key, "corr-676b-replay");
            Assert.Equal(creada.IdSolicitud, replay.IdSolicitud);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CrearSolicitudCupoAsync(ids[1], key, "corr-676b-otro"));
            Assert.Equal("Idempotency-Key ya utilizada para otra solicitud.", ex.Message);

            await using var v = NuevoContexto(cs);
            var montoNulo = await v.Database.SqlQueryRaw<int>(
                "SELECT CASE WHEN monto_solicitado IS NULL THEN 1 ELSE 0 END AS Value FROM dbo.cartera_solicitudes_cupo WHERE id_solicitud = {0}",
                creada.IdSolicitud).SingleAsync();
            Assert.Equal(1, montoNulo);

            var s = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == creada.IdSolicitud);
            var motor = CarteraPolicyParameters.Vigente();
            Assert.Equal(motor.CupoMinimo, s.CupoMinimoAplicado);
            Assert.Equal(motor.CupoMaximo, s.CupoMaximoAplicado);
            Assert.Equal(CarteraPoliticaAplicada.EdadElegibleMinima, s.EdadMinimaAplicada);
            Assert.Equal(CarteraPoliticaAplicada.EdadElegibleMaxima, s.EdadMaximaAplicada);
            Assert.Null(s.ScoreDatacreditoMinimoAplicado);
            Assert.Equal(1, await v.CarteraSolicitudCupoIntentos.CountAsync(i => i.IdSolicitud == creada.IdSolicitud));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para la prueba SQL de estados activos de M2.4d.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, List<long> personas, List<long> usuarios, List<long> solicitudes)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud IN (SELECT id_solicitud FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id})");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id}");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch
        {
            // best-effort
        }
    }
}
