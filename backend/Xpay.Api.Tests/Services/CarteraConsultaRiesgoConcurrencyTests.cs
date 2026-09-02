using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// M2.3b2 — cobertura de integración de CONCURRENCIA del orquestador de consulta
// de riesgo contra SQL Server REAL (el contenedor efímero del pipeline CI).
// Cierra M2_3A_SQL_CONCURRENCY_DEBT únicamente cuando estos tests corren y
// pasan en CI.
//
// SIN red, SIN proveedor real, SIN token, SIN credenciales de proveedor, SIN
// cédulas: se construye CarteraConsultaRiesgoService directamente con el store
// EF real + FakeMiDecisorClient (imposible por construcción llegar a red).
//
// Local sin `ConnectionStrings__XpayConnection`: los dos [Fact] retornan
// temprano y CUENTAN COMO PASS (no SKIP — xunit 2.9.2 no tiene skip dinámico).
// En CI la variable la fija el step `dotnet test` de backend-validation.yml;
// si allí faltara, el test FALLA (no falso verde).

[CollectionDefinition("SqlIntegration", DisableParallelization = true)]
public sealed class SqlIntegrationCollection { }

[Collection("SqlIntegration")]
public sealed class CarteraConsultaRiesgoConcurrencyTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // Resultado normalizado sintético que el fake MiDecisor devuelve. Los
    // valores crudos ("777" / "ALTA" / "A" / "1500000") NO derivan de ninguna
    // lista autorizada — son marcadores obviamente ficticios.
    private static readonly MiDecisorResultado ResultadoSintetico =
        new("ACCEPTED", "202 ACCEPTED", ConInformacion: true,
            ScoreRaw: "777", Viabilidad: "ALTA", RatingRecaudos: "A",
            MontoSugeridoRaw: "1500000", AlertasCount: 0);

    // ── T1 — MISMA solicitud, dos ejecuciones concurrentes ────────────────
    // Sólo una gana TX-A (sp_getapplock + guard de estado durable); sólo una
    // cruza ENVIO_INCIERTO y llama al proveedor; una sola finalización durable.
    [Fact]
    public async Task SameSolicitud_TwoConcurrentExecutions_OnlyOneWins_SingleProviderCall()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            var (_, idUsuario, idSolicitud) = await SembrarCasoAsync(cs, idUnidad, idPolitica, creados);

            var client = new FakeMiDecisorClient(ResultadoSintetico);
            var barrera = new BarreraAutorizacion(2);
            var reloj = new FijoTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            await using var ctx1 = NuevoContexto(cs);
            await using var ctx2 = NuevoContexto(cs);
            var s1 = new CarteraConsultaRiesgoService(new CarteraConsultaRiesgoStore(ctx1), client, barrera, reloj, new CapturingLogger());
            var s2 = new CarteraConsultaRiesgoService(new CarteraConsultaRiesgoStore(ctx2), client, barrera, reloj, new CapturingLogger());

            var res = await Task.WhenAll(
                Correr(s1, idSolicitud, idUsuario, "conc-a-" + Guid.NewGuid()),
                Correr(s2, idSolicitud, idUsuario, "conc-b-" + Guid.NewGuid()));

            var exitosos = res.Count(r => r.ok is not null);
            var fallidos = res.Where(r => r.err is not null).ToList();

            Assert.Equal(1, exitosos);
            Assert.Single(fallidos);
            Assert.IsAssignableFrom<InvalidOperationException>(fallidos[0].err);
            Assert.Equal(1, client.CallCount);

            var ganador = res.Single(r => r.ok is not null).ok!;
            Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, ganador.EstadoSolicitud);
            Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, ganador.ResultadoTecnico);
            Assert.True(ganador.EsResultadoUtil);

            // ── verificación durable con un contexto fresco ──────────────
            await using var ctx3 = NuevoContexto(cs);

            var sol = await ctx3.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == idSolicitud);
            Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, sol.EstadoSolicitud);
            // Las columnas de decisión de la solicitud NO se tocan en b1/b2.
            Assert.Null(sol.ScoreObservado);
            Assert.Null(sol.MontoSugeridoObservado);
            Assert.Null(sol.EstadoScore);
            Assert.Null(sol.ViabilidadObservada);
            Assert.Null(sol.RatingRecaudosObservado);
            Assert.Equal("PENDIENTE", sol.DecisionCrediticia);

            var intentos = await ctx3.CarteraSolicitudCupoIntentos.AsNoTracking()
                .Where(i => i.IdSolicitud == idSolicitud).ToListAsync();
            var it = Assert.Single(intentos);
            Assert.Equal(1, it.NumeroIntento);
            Assert.Equal(CarteraIntentoFases.Finalizado, it.FaseIntento);
            Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, it.ResultadoTecnico);
            Assert.True(it.EsIntentoConResultadoUtil);
            Assert.True(it.ConInformacion);
            Assert.Equal("777", it.ScoreRaw);
            Assert.Equal("ALTA", it.ViabilidadRaw);
            Assert.Equal("A", it.RatingRecaudosRaw);
            Assert.Equal("1500000", it.MontoSugeridoRaw);
            Assert.Equal(0, it.AlertasCount);
            Assert.Equal(200, it.HttpStatusObservado);
            Assert.Equal("202 ACCEPTED", it.ContentStatusObservado);
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ── T2 — DOS solicitudes distintas, dos ejecuciones concurrentes ──────
    // El AppLock se particiona por idSolicitud → ambas proceden, dos llamadas
    // al proveedor, dos finalizaciones durables.
    [Fact]
    public async Task DifferentSolicitudes_TwoConcurrentExecutions_BothProceed_TwoProviderCalls()
    {
        if (!TryConnString(out var cs)) return;

        var idUnidad = await LeerIdUnidadAsync(cs);
        var idPolitica = await LeerIdPoliticaActivaAsync(cs);
        var creados = new Sembrados();
        try
        {
            // Dos usuarios distintos: el índice UNIQUE filtrado de "solicitud
            // activa por usuario" prohíbe dos solicitudes activas del mismo.
            var a = await SembrarCasoAsync(cs, idUnidad, idPolitica, creados);
            var b = await SembrarCasoAsync(cs, idUnidad, idPolitica, creados);

            var client = new FakeMiDecisorClient(ResultadoSintetico);
            var barrera = new BarreraAutorizacion(2);
            var reloj = new FijoTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            await using var ctx1 = NuevoContexto(cs);
            await using var ctx2 = NuevoContexto(cs);
            var s1 = new CarteraConsultaRiesgoService(new CarteraConsultaRiesgoStore(ctx1), client, barrera, reloj, new CapturingLogger());
            var s2 = new CarteraConsultaRiesgoService(new CarteraConsultaRiesgoStore(ctx2), client, barrera, reloj, new CapturingLogger());

            var res = await Task.WhenAll(
                Correr(s1, a.solicitud, a.usuario, "conc-a-" + Guid.NewGuid()),
                Correr(s2, b.solicitud, b.usuario, "conc-b-" + Guid.NewGuid()));

            Assert.All(res, r => Assert.Null(r.err));
            Assert.All(res, r =>
            {
                Assert.NotNull(r.ok);
                Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, r.ok!.EstadoSolicitud);
                Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, r.ok.ResultadoTecnico);
                Assert.True(r.ok.EsResultadoUtil);
            });
            Assert.Equal(2, client.CallCount);

            await using var ctx3 = NuevoContexto(cs);
            foreach (var idSolicitud in new[] { a.solicitud, b.solicitud })
            {
                var sol = await ctx3.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(x => x.IdSolicitud == idSolicitud);
                Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, sol.EstadoSolicitud);

                var intentos = await ctx3.CarteraSolicitudCupoIntentos.AsNoTracking()
                    .Where(i => i.IdSolicitud == idSolicitud).ToListAsync();
                var it = Assert.Single(intentos);
                Assert.Equal(CarteraIntentoFases.Finalizado, it.FaseIntento);
                Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, it.ResultadoTecnico);
                Assert.True(it.EsIntentoConResultadoUtil);
            }
        }
        finally
        {
            await LimpiarAsync(cs, creados);
        }
    }

    // ════════════════════ infraestructura de test ════════════════════════

    // true → hay connection string real, ejecutar el test SQL.
    // false → local sin SQL configurado: early-return que xUnit cuenta como
    //         PASS (no SKIP). Si falta en CI → Assert.False falla el test.
    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;

        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi,
            $"{EnvConnString} es obligatoria en CI para las pruebas de concurrencia SQL de M2.3b2.");
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

    // Crea Persona + Usuario + Solicitud(RECIBIDA) + Intento #1(PRE_CALL)
    // sintéticos y registra los IDs generados para el cleanup.
    private static async Task<(long persona, long usuario, long solicitud)> SembrarCasoAsync(
        string cs, long idUnidad, long idPolitica, Sembrados creados)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        // Documento sintético de 9 dígitos ASCII (rango 77xxxxxxx): fuera de los
        // rangos de 008_seed_qa_dataset.sql, del fixture CI y de los generados
        // por validate-backend.sh. El mapper exige 3–13 dígitos ASCII.
        var doc = $"77{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}";

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad,
            TipoDocumento   = "CC",
            NumeroDocumento = doc,
            PrimerNombre    = "ConcTest",
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
            NombreUsuario = $"conc_test_{sufijo}",
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
            EstadoSolicitud    = CarteraSolicitudCupoEstados.Recibida,
            DecisionCrediticia = "PENDIENTE",
            IdPoliticaAplicada = idPolitica,
            CupoMinimoAplicado = 0m,
            CupoMaximoAplicado = 1_000_000m,
            EdadMinimaAplicada = 18,
            EdadMaximaAplicada = 99,
            NumeroIntento      = 1,
            CorrelationId      = $"conc-sol-{sufijo}",
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
            FechaInicio               = ahora,
            CorrelationId             = $"conc-int-{sufijo}",
            EsIntentoConResultadoUtil = false,
            FaseIntento               = CarteraIntentoFases.PreCall,
        });
        await ctx.SaveChangesAsync();

        return (persona.IdPersona, usuario.IdUsuario, solicitud.IdSolicitud);
    }

    // Borra sólo los IDs creados por esta ejecución, en orden FK. Best-effort:
    // la BD de CI es efímera por job. Maneja colecciones parciales.
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
        catch
        {
            // teardown best-effort — no se propaga si los asserts ya corrieron.
        }
    }

    private static async Task<(ConsultaRiesgoResultado? ok, Exception? err)> Correr(
        CarteraConsultaRiesgoService svc, long idSolicitud, long idUsuario, string correlationId)
    {
        try
        {
            var r = await svc.EjecutarConsultaRiesgoAsync(idSolicitud, idUsuario, correlationId);
            return (r, null);
        }
        catch (Exception e)
        {
            return (null, e);
        }
    }

    // Autorización test-only: barrera ANTES de TX-A. Ambas ejecuciones completan
    // el pre-flight y quedan liberadas casi simultáneamente para competir de
    // verdad por sp_getapplock. NO sincroniza dentro del fake MiDecisor (allí
    // sólo una debe entrar).
    private sealed class BarreraAutorizacion : IConsultaRiesgoAutorizacion
    {
        private static readonly TimeSpan Espera = TimeSpan.FromSeconds(30);
        private readonly int _esperados;
        private int _llegadas;
        private readonly TaskCompletionSource _todosListos =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BarreraAutorizacion(int esperados) => _esperados = esperados;

        public async Task<bool> TieneAutorizacionVigenteAsync(
            long idUsuario, long idSolicitud, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _llegadas) >= _esperados)
                _todosListos.TrySetResult();

            using var cts = new CancellationTokenSource(Espera);
            try
            {
                await _todosListos.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Barrera de concurrencia: no llegaron {_esperados} ejecuciones al pre-flight en {Espera.TotalSeconds}s.");
            }

            return true;
        }
    }
}
