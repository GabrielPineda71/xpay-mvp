using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// M2.4d — tests del orquestador resume-aware. SIN red, SIN proveedor real, SIN
// SQL. El servicio de consulta se construye REAL con los fakes existentes
// (FakeCarteraConsultaRiesgoStore / FakeMiDecisorClient / FakeConsultaRiesgoAutorizacion)
// para poder afirmar "0 llamadas al proveedor". Las primitivas downstream son
// fakes de sus interfaces.
public sealed class CarteraDecisionCrediticiaOrchestratorTests
{
    private const long IdSolicitud = 10;
    private const long IdUsuario   = 7;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ── fakes downstream ──────────────────────────────────────────────────
    private sealed class FakeReader : ICarteraSolicitudEvaluacionReader
    {
        public CarteraSolicitudEvaluacionSnapshot? Snapshot { get; set; }
        public int Calls { get; private set; }

        public Task<CarteraSolicitudEvaluacionSnapshot?> LeerAsync(
            long idSolicitud, long idUsuario, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Snapshot is null || Snapshot.IdUsuario != idUsuario)
                return Task.FromResult<CarteraSolicitudEvaluacionSnapshot?>(null);
            return Task.FromResult<CarteraSolicitudEvaluacionSnapshot?>(Snapshot);
        }
    }

    private sealed class FakeConsumo : ICarteraResultadoRiesgoConsumo
    {
        public ResultadoConsumoRiesgo Resultado = ResultadoConsumoRiesgo.Consumido;
        public int Calls { get; private set; }

        public Task<ResultadoConsumoRiesgo> ConsumirResultadoRiesgoAsync(
            long idSolicitud, int numeroIntento, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Resultado);
        }
    }

    private sealed class FakeDecision : ICarteraDecisionCrediticia
    {
        public ResultadoAplicacionDecision Resultado = ResultadoAplicacionDecision.Aplicada;
        public Func<Task>? OnCall;
        public int Calls { get; private set; }

        public async Task<ResultadoAplicacionDecision> AplicarDecisionAsync(
            long idSolicitud, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (OnCall is not null) await OnCall();
            return Resultado;
        }
    }

    private sealed class FakeMaterializacion : ICarteraMaterializacionCupo
    {
        public ResultadoMaterializacionCupo Resultado = ResultadoMaterializacionCupo.Materializado;
        public Func<Task>? OnCall;
        public int Calls { get; private set; }

        public async Task<ResultadoMaterializacionCupo> MaterializarCupoAsync(
            long idSolicitud, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (OnCall is not null) await OnCall();
            return Resultado;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private static CarteraSolicitudEvaluacionSnapshot Snap(
        string estado, string decision = "PENDIENTE", decimal? monto = null, DateTime? fechaDecision = null, long? idCupo = null)
        => new(IdSolicitud, IdUsuario, estado, 1, decision, monto, fechaDecision, null, idCupo);

    private static Persona PersonaValida() => new()
    {
        IdPersona = 3, TipoDocumento = "CC", NumeroDocumento = "1234567", PrimerApellido = "Rodriguez",
    };

    private sealed record Rig(
        CarteraDecisionCrediticiaOrchestrator Orq,
        FakeReader Reader,
        FakeMiDecisorClient Client,
        FakeCarteraConsultaRiesgoStore RiesgoStore,
        FakeConsumo Consumo,
        FakeDecision Decision,
        FakeMaterializacion Materializacion);

    private static Rig Crear(string estado, bool autoriza = false, Persona? persona = null)
    {
        var reader  = new FakeReader { Snapshot = Snap(estado) };
        var client  = new FakeMiDecisorClient(new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, "853", "ALTA", "A", "1", 0));
        var rStore  = new FakeCarteraConsultaRiesgoStore
        {
            Contexto = new ConsultaRiesgoContexto(IdSolicitud, IdUsuario, 3, estado, persona ?? PersonaValida()),
        };
        var svc = new CarteraConsultaRiesgoService(
            rStore, client, new FakeConsultaRiesgoAutorizacion(autoriza),
            new FijoTimeProvider(T0), new CapturingLogger());

        var consumo = new FakeConsumo();
        var decision = new FakeDecision();
        var mat = new FakeMaterializacion();

        var orq = new CarteraDecisionCrediticiaOrchestrator(
            reader, svc, consumo, decision, mat,
            NullLogger<CarteraDecisionCrediticiaOrchestrator>.Instance);

        return new Rig(orq, reader, client, rStore, consumo, decision, mat);
    }

    // 1 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Recibida_autorizacionFalse_ceroLlamadasProveedor()
    {
        var r = Crear(CarteraSolicitudCupoEstados.Recibida, autoriza: false);

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(OrquestacionEvaluacionEstado.AutorizacionNoDisponible, res.Resultado);
        Assert.Equal(0, r.Client.CallCount);
        Assert.Equal(0, r.RiesgoStore.IniciarCalls);
        Assert.Equal(0, r.RiesgoStore.MarcarCalls);
        Assert.Equal(0, r.Consumo.Calls);
        Assert.Equal(0, r.Decision.Calls);
        Assert.Equal(0, r.Materializacion.Calls);
    }

    // 2 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Recibida_autorizacionFalse_estadoNoAvanza()
    {
        var r = Crear(CarteraSolicitudCupoEstados.Recibida, autoriza: false);

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraSolicitudCupoEstados.Recibida, res.EstadoSolicitud);
        Assert.Equal(CarteraSolicitudCupoEstados.Recibida, r.Reader.Snapshot!.EstadoSolicitud);
    }

    // 3 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConsultandoRiesgo_ceroProveedor_requiereReconciliacion()
    {
        var r = Crear(CarteraSolicitudCupoEstados.ConsultandoRiesgo);

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(OrquestacionEvaluacionEstado.RequiereReconciliacion, res.Resultado);
        Assert.Equal(0, r.Client.CallCount);
        Assert.Equal(0, r.Consumo.Calls);
        Assert.Equal(0, r.Decision.Calls);
        Assert.Equal(0, r.Materializacion.Calls);
    }

    // 4 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task EnEvaluacion_reanudaConsumoYDecision_sinProveedor()
    {
        var r = Crear(CarteraSolicitudCupoEstados.EnEvaluacion);
        r.Consumo.Resultado  = ResultadoConsumoRiesgo.Consumido;
        r.Decision.Resultado = ResultadoAplicacionDecision.Aplicada;
        r.Decision.OnCall = () =>
        {
            r.Reader.Snapshot = Snap(CarteraSolicitudCupoEstados.Rechazada, "RECHAZADA", 0m, T0.UtcDateTime);
            return Task.CompletedTask;
        };

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(0, r.Client.CallCount);
        Assert.Equal(1, r.Consumo.Calls);
        Assert.Equal(1, r.Decision.Calls);
        Assert.Equal(CarteraSolicitudCupoEstados.Rechazada, res.EstadoSolicitud);
    }

    // 5 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task EnEvaluacion_yaConsumido_continua()
    {
        var r = Crear(CarteraSolicitudCupoEstados.EnEvaluacion);
        r.Consumo.Resultado  = ResultadoConsumoRiesgo.YaConsumido;
        r.Decision.Resultado = ResultadoAplicacionDecision.Aplicada;
        r.Decision.OnCall = () =>
        {
            r.Reader.Snapshot = Snap(CarteraSolicitudCupoEstados.PendienteRevisionManual, "NO_DECIDIBLE", null, T0.UtcDateTime);
            return Task.CompletedTask;
        };

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(1, r.Consumo.Calls);
        Assert.Equal(1, r.Decision.Calls);
        Assert.True(res.RequiereRevisionManual);
        Assert.Equal(0, r.Client.CallCount);
    }

    // 6 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task EnEvaluacion_yaDecidido_continuaAMaterializacion()
    {
        var r = Crear(CarteraSolicitudCupoEstados.EnEvaluacion);
        r.Consumo.Resultado  = ResultadoConsumoRiesgo.YaConsumido;
        r.Decision.Resultado = ResultadoAplicacionDecision.YaDecidido;
        r.Decision.OnCall = () =>
        {
            r.Reader.Snapshot = Snap(CarteraSolicitudCupoEstados.AprobadaPendienteCupo, "APROBADA", 400_000m, T0.UtcDateTime);
            return Task.CompletedTask;
        };
        r.Materializacion.Resultado = ResultadoMaterializacionCupo.YaMaterializado;
        r.Materializacion.OnCall = () =>
        {
            r.Reader.Snapshot = Snap(CarteraSolicitudCupoEstados.Aprobada, "APROBADA", 400_000m, T0.UtcDateTime, idCupo: 55);
            return Task.CompletedTask;
        };

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(1, r.Decision.Calls);
        Assert.Equal(1, r.Materializacion.Calls);
        Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, res.EstadoSolicitud);
        Assert.Equal(0, r.Client.CallCount);
    }

    // 7 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AprobadaPendienteCupo_soloMaterializacion()
    {
        var r = Crear(CarteraSolicitudCupoEstados.AprobadaPendienteCupo);
        r.Reader.Snapshot = Snap(CarteraSolicitudCupoEstados.AprobadaPendienteCupo, "APROBADA", 400_000m, T0.UtcDateTime);
        r.Materializacion.Resultado = ResultadoMaterializacionCupo.Materializado;
        r.Materializacion.OnCall = () =>
        {
            r.Reader.Snapshot = Snap(CarteraSolicitudCupoEstados.Aprobada, "APROBADA", 400_000m, T0.UtcDateTime, idCupo: 55);
            return Task.CompletedTask;
        };

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(0, r.Consumo.Calls);
        Assert.Equal(0, r.Decision.Calls);
        Assert.Equal(1, r.Materializacion.Calls);
        Assert.Equal(0, r.Client.CallCount);
        Assert.Equal(OrquestacionEvaluacionEstado.Procesada, res.Resultado);
        Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, res.EstadoSolicitud);
    }

    // 8/9/10/11 ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData(CarteraSolicitudCupoEstados.Aprobada)]
    [InlineData(CarteraSolicitudCupoEstados.Rechazada)]
    [InlineData(CarteraSolicitudCupoEstados.PendienteRevisionManual)]
    [InlineData(CarteraSolicitudCupoEstados.ErrorProveedor)]
    public async Task EstadoTerminal_sinEfectos(string estado)
    {
        var r = Crear(estado);

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(OrquestacionEvaluacionEstado.Terminal, res.Resultado);
        Assert.Equal(estado, res.EstadoSolicitud);
        Assert.Equal(0, r.Client.CallCount);
        Assert.Equal(0, r.Consumo.Calls);
        Assert.Equal(0, r.Decision.Calls);
        Assert.Equal(0, r.Materializacion.Calls);
    }

    // 12 ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DobleInvocacion_noDuplicaDecisionNiCupo()
    {
        var r = Crear(CarteraSolicitudCupoEstados.EnEvaluacion);
        r.Consumo.Resultado = ResultadoConsumoRiesgo.Consumido;
        r.Decision.Resultado = ResultadoAplicacionDecision.Aplicada;
        r.Decision.OnCall = () =>
        {
            // primera vez APROBADA_PENDIENTE_CUPO; luego idempotente
            r.Reader.Snapshot = Snap(CarteraSolicitudCupoEstados.AprobadaPendienteCupo, "APROBADA", 400_000m, T0.UtcDateTime);
            r.Consumo.Resultado = ResultadoConsumoRiesgo.YaConsumido;
            r.Decision.Resultado = ResultadoAplicacionDecision.YaDecidido;
            return Task.CompletedTask;
        };
        r.Materializacion.Resultado = ResultadoMaterializacionCupo.Materializado;
        r.Materializacion.OnCall = () =>
        {
            r.Reader.Snapshot = Snap(CarteraSolicitudCupoEstados.Aprobada, "APROBADA", 400_000m, T0.UtcDateTime, idCupo: 55);
            r.Materializacion.Resultado = ResultadoMaterializacionCupo.YaMaterializado;
            return Task.CompletedTask;
        };

        var res1 = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");
        var res2 = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, res1.EstadoSolicitud);
        Assert.Equal(OrquestacionEvaluacionEstado.Terminal, res2.Resultado);
        Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, res2.EstadoSolicitud);
        Assert.Equal(1, r.Materializacion.Calls); // la 2ª invocación ve APROBADA terminal → no vuelve a materializar
        Assert.Equal(0, r.Client.CallCount);
    }

    // 23 — ownership: usuario ajeno no ve la solicitud ───────────────────
    [Fact]
    public async Task UsuarioAjeno_noEncontrada()
    {
        var r = Crear(CarteraSolicitudCupoEstados.EnEvaluacion);

        var res = await r.Orq.EvaluarAsync(IdSolicitud, idUsuario: 999, "corr");

        Assert.Equal(OrquestacionEvaluacionEstado.NoEncontrada, res.Resultado);
        Assert.Equal(0, r.Client.CallCount);
        Assert.Equal(0, r.Consumo.Calls);
        Assert.Equal(0, r.Decision.Calls);
    }
}
