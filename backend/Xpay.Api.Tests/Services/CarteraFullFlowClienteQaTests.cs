using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-702 - FULL_FLOW offline con el GATE REAL en modo QA_CLIENT_FULL_FLOW:
// una sola evaluacion desde RECIBIDA llega a consulta -> consumo -> decision
// -> materializacion; una segunda evaluacion no vuelve a consultar.
public sealed class CarteraFullFlowClienteQaTests
{
    private const long IdSolicitud = 10;
    private const long IdUsuario   = 7;

    private sealed class Estado { public string Valor = CarteraSolicitudCupoEstados.Recibida; }

    private sealed class Reader(Estado e, FakeCarteraConsultaRiesgoStore r) : ICarteraSolicitudEvaluacionReader
    {
        public Task<CarteraSolicitudEvaluacionSnapshot?> LeerAsync(long s, long u, CancellationToken ct = default)
        {
            if (e.Valor == CarteraSolicitudCupoEstados.Recibida && r.UltimoOutcome is { } o)
                e.Valor = o.EstadoSolicitudFinal;
            return Task.FromResult<CarteraSolicitudEvaluacionSnapshot?>(
                new(IdSolicitud, IdUsuario, e.Valor, 1, "PENDIENTE", null, null, null, null));
        }
    }

    private sealed class Consumo : ICarteraResultadoRiesgoConsumo
    {
        public int Calls;
        public Task<ResultadoConsumoRiesgo> ConsumirResultadoRiesgoAsync(long s, int n, CancellationToken ct = default)
        { Calls++; return Task.FromResult(ResultadoConsumoRiesgo.Consumido); }
    }

    private sealed class Decision(Estado e) : ICarteraDecisionCrediticia
    {
        public int Calls;
        public Task<ResultadoAplicacionDecision> AplicarDecisionAsync(long s, CancellationToken ct = default)
        {
            Calls++;
            e.Valor = CarteraSolicitudCupoEstados.AprobadaPendienteCupo;
            return Task.FromResult(ResultadoAplicacionDecision.Aplicada);
        }
    }

    private sealed class Materializacion(Estado e) : ICarteraMaterializacionCupo
    {
        public int Calls;
        public Task<ResultadoMaterializacionCupo> MaterializarCupoAsync(long s, CancellationToken ct = default)
        {
            Calls++;
            e.Valor = CarteraSolicitudCupoEstados.Aprobada;
            return Task.FromResult(ResultadoMaterializacionCupo.Materializado);
        }
    }

    // Marca QA: el conteo de consultas reales sigue al store de consulta.
    private sealed class MarcaQa(FakeCarteraConsultaRiesgoStore r, int previas) : IQaClienteRiesgoStore
    {
        public Task<bool> EsClienteQaPreparadoAsync(long u, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> ContarConsultasRealesAsync(long u, CancellationToken ct = default)
            => Task.FromResult(previas + r.FinalizarCalls);
    }

    private sealed record Rig(
        CarteraDecisionCrediticiaOrchestrator Orq, FakeMiDecisorClient Client,
        Consumo Consumo, Decision Decision, Materializacion Mat, Estado Estado);

    private static Rig Crear(string stop, int consultasPrevias = 0)
    {
        var estado = new Estado();
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, "853", "ALTA", "A", "1", 0) { HttpStatus = 200 });
        var persona = new Persona
        {
            IdPersona = 3, TipoDocumento = "CC", NumeroDocumento = "1234567", PrimerApellido = "Sintetico",
        };
        var rStore = new FakeCarteraConsultaRiesgoStore
        {
            Contexto = new ConsultaRiesgoContexto(IdSolicitud, IdUsuario, 3, CarteraSolicitudCupoEstados.Recibida, persona),
        };

        var cfg = new FakeConfiguration(new Dictionary<string, string?>
        {
            [MiDecisorQaGateConfig.EnvGateMode]          = MiDecisorQaGateConfig.ModoClienteQaFullFlow,
            [MiDecisorQaGateConfig.EnvStopAfterConsulta] = stop,
        });
        var gate = new AutorizacionConsultaRiesgoQaGate(
            new FakeConsultaRiesgoAutorizacion(true), cfg,
            NullLogger<AutorizacionConsultaRiesgoQaGate>.Instance,
            new MarcaQa(rStore, consultasPrevias));

        var svc = new CarteraConsultaRiesgoService(
            rStore, client, gate, new FijoTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new CapturingLogger());

        var consumo = new Consumo();
        var decision = new Decision(estado);
        var mat = new Materializacion(estado);
        var orq = new CarteraDecisionCrediticiaOrchestrator(
            new Reader(estado, rStore), svc, consumo, decision, mat, cfg,
            Xpay.Api.Tests.Common.AmbienteXpayTests.Ambiente("QA"),
            NullLogger<CarteraDecisionCrediticiaOrchestrator>.Instance);
        return new Rig(orq, client, consumo, decision, mat, estado);
    }

    [Fact]
    public async Task FullFlow_UnaEvaluacion_LlegaADecisionYMaterializacion_ConUnaSolaConsulta()
    {
        var r = Crear(stop: "false");

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(OrquestacionEvaluacionEstado.Procesada, res.Resultado);
        Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, r.Estado.Valor);
        Assert.Equal(1, r.Client.CallCount);
        Assert.Equal(1, r.Consumo.Calls);
        Assert.Equal(1, r.Decision.Calls);
        Assert.Equal(1, r.Mat.Calls);

        // Retry / doble clic: no hay segunda consulta al proveedor.
        var res2 = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr-2");
        Assert.Equal(OrquestacionEvaluacionEstado.Terminal, res2.Resultado);
        Assert.Equal(1, r.Client.CallCount);
        Assert.Equal(1, r.Decision.Calls);
        Assert.Equal(1, r.Mat.Calls);
    }

    [Fact]
    public async Task FullFlow_ClienteQaConConsultaPrevia_NoConsulta()
    {
        var r = Crear(stop: "false", consultasPrevias: 1);

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(OrquestacionEvaluacionEstado.AutorizacionNoDisponible, res.Resultado);
        Assert.Equal(0, r.Client.CallCount);
        Assert.Equal(CarteraSolicitudCupoEstados.Recibida, r.Estado.Valor);
    }

    [Fact]
    public async Task ModoClienteQa_ConStopTrue_FailClosed_SinConsulta()
    {
        var r = Crear(stop: "true");

        var res = await r.Orq.EvaluarAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(OrquestacionEvaluacionEstado.AutorizacionNoDisponible, res.Resultado);
        Assert.Equal(0, r.Client.CallCount);
    }
}
