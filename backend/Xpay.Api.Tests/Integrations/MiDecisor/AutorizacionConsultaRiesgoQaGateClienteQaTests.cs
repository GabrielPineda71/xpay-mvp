using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Tests.Services;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

// XPAY-702 - modo QA_CLIENT_FULL_FLOW del gate QA. Offline: sin red, sin DB,
// sin cedulas. Store de marcas y autorizacion durable sustituidos por fakes.
public sealed class AutorizacionConsultaRiesgoQaGateClienteQaTests
{
    private const long IdUsuario   = 7;
    private const long IdSolicitud = 10;

    private sealed class FakeStore : IQaClienteRiesgoStore
    {
        public bool EsClienteQa = true;
        public int Consultas;
        public bool Lanza;
        public int MarcaCalls;
        public int ConteoCalls;

        public Task<bool> EsClienteQaPreparadoAsync(long idUsuario, CancellationToken ct = default)
        {
            MarcaCalls++;
            if (Lanza)
                throw new InvalidOperationException("db caida");
            return Task.FromResult(EsClienteQa);
        }

        public Task<int> ContarConsultasRealesAsync(long idUsuario, CancellationToken ct = default)
        {
            ConteoCalls++;
            return Task.FromResult(Consultas);
        }
    }

    private static Dictionary<string, string?> ConfigQa() => new()
    {
        [MiDecisorQaGateConfig.EnvGateMode]          = MiDecisorQaGateConfig.ModoClienteQaFullFlow,
        [MiDecisorQaGateConfig.EnvStopAfterConsulta] = "false",
    };

    private static (AutorizacionConsultaRiesgoQaGate Gate, FakeConsultaRiesgoAutorizacion Durable) Crear(
        Dictionary<string, string?> config, FakeStore? store, bool durableAutoriza = true)
    {
        var durable = new FakeConsultaRiesgoAutorizacion(durableAutoriza);
        var logger  = new CapturingLogger<AutorizacionConsultaRiesgoQaGate>();
        var gate    = new AutorizacionConsultaRiesgoQaGate(durable, new FakeConfiguration(config), logger, store);
        return (gate, durable);
    }

    [Fact]
    public async Task ClienteQa_SinConsultas_StopFalse_Consentimiento_Autoriza()
    {
        var store = new FakeStore();
        var (gate, durable) = Crear(ConfigQa(), store);

        Assert.True(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(1, durable.CallCount);
    }

    [Fact]
    public async Task ClienteQa_SinConsentimiento_NoAutoriza()
    {
        var (gate, durable) = Crear(ConfigQa(), new FakeStore(), durableAutoriza: false);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(1, durable.CallCount);
    }

    [Fact]
    public async Task UsuarioNormal_SinMarcaQa_NoAutoriza_NiConsultaDurable()
    {
        var store = new FakeStore { EsClienteQa = false };
        var (gate, durable) = Crear(ConfigQa(), store);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
        Assert.Equal(0, store.ConteoCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ClienteQa_ConConsultaRealPrevia_NoAutoriza(int consultas)
    {
        var store = new FakeStore { Consultas = consultas };
        var (gate, durable) = Crear(ConfigQa(), store);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("False")]
    [InlineData(" false")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ModoQa_StopNoEsExactamenteFalse_NoAutoriza_SinTocarStore(string? stop)
    {
        var config = ConfigQa();
        config[MiDecisorQaGateConfig.EnvStopAfterConsulta] = stop;
        var store = new FakeStore();
        var (gate, durable) = Crear(config, store);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, store.MarcaCalls);
        Assert.Equal(0, durable.CallCount);
    }

    [Theory]
    [InlineData("qa_client_full_flow")]
    [InlineData("FULL_FLOW")]
    [InlineData(" QA_CLIENT_FULL_FLOW")]
    [InlineData("SOLICITUD_ALLOWLIST")]
    public async Task ModoDesconocido_NoAutoriza_SinTocarStore(string modo)
    {
        var config = ConfigQa();
        config[MiDecisorQaGateConfig.EnvGateMode] = modo;
        var store = new FakeStore();
        var (gate, durable) = Crear(config, store);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, store.MarcaCalls);
        Assert.Equal(0, durable.CallCount);
    }

    [Fact]
    public async Task ModoQa_SinStore_NoAutoriza()
    {
        var (gate, durable) = Crear(ConfigQa(), store: null);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }

    [Fact]
    public async Task ModoQa_StoreLanza_NoAutoriza_FailClosed()
    {
        var store = new FakeStore { Lanza = true };
        var (gate, durable) = Crear(ConfigQa(), store);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }

    // El modo QA no depende de MIDECISOR_QA_ALLOWED_SOLICITUD_ID: no hace
    // falta tocar Azure por cada cliente QA.
    [Fact]
    public async Task ModoQa_IgnoraAllowlistPorSolicitud()
    {
        var config = ConfigQa();
        config[MiDecisorQaGateConfig.EnvAllowedSolicitudId] = "999";
        var (gate, durable) = Crear(config, new FakeStore());

        Assert.True(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(1, durable.CallCount);
    }

    // Sin modo (o modo vacio): rama heredada XPAY-656 intacta; store intacto.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SinModo_RamaHeredada_AutorizaComoAntes_SinTocarStore(string? modo)
    {
        var config = new Dictionary<string, string?>
        {
            [MiDecisorQaGateConfig.EnvAllowedSolicitudId] = "10",
            [MiDecisorQaGateConfig.EnvStopAfterConsulta]  = "true",
            [MiDecisorQaGateConfig.EnvGateMode]           = modo,
        };
        var store = new FakeStore();
        var (gate, durable) = Crear(config, store);

        Assert.True(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(1, durable.CallCount);
        Assert.Equal(0, store.MarcaCalls);
    }

    [Fact]
    public async Task SinModo_RamaHeredada_StopFalse_SigueSinAutorizar()
    {
        var config = new Dictionary<string, string?>
        {
            [MiDecisorQaGateConfig.EnvAllowedSolicitudId] = "10",
            [MiDecisorQaGateConfig.EnvStopAfterConsulta]  = "false",
        };
        var (gate, durable) = Crear(config, new FakeStore());

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }
}
