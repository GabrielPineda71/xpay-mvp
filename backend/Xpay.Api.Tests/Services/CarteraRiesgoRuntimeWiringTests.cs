using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xpay.Api.Tests.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Services;

// M2.4d / XPAY-656 — el wiring de runtime registra el pipeline y liga
// IConsultaRiesgoAutorizacion al gate QA fail-closed
// AutorizacionConsultaRiesgoQaGate, cuya dependencia interna es la autorización
// durable concreta. Sin configuración QA el gate no autoriza ninguna solicitud.
// Los tests de descriptores no construyen el contenedor; el de resolución usa
// un XpayDbContext con una cadena de conexión ficticia que NUNCA se abre (sin
// configuración el gate devuelve false antes de consultar la durable).
public sealed class CarteraRiesgoRuntimeWiringTests
{
    private static ServiceCollection Wired()
    {
        var services = new ServiceCollection();
        services.AddCarteraRiesgoRuntime();
        return services;
    }

    // ── XPAY-656 — barrera de autorización ────────────────────────────────
    [Fact]
    public void Autorizacion_es_un_unico_registro_por_factory_y_no_el_stub()
    {
        var services = Wired();

        var autz = services.Where(d => d.ServiceType == typeof(IConsultaRiesgoAutorizacion)).ToList();

        Assert.Single(autz);
        Assert.Equal(ServiceLifetime.Scoped, autz[0].Lifetime);
        Assert.NotNull(autz[0].ImplementationFactory);
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(AutorizacionConsultaRiesgoNoDisponible));
    }

    [Fact]
    public void Durable_se_registra_solo_como_tipo_concreto()
    {
        var services = Wired();

        var durable = services.Where(d => d.ImplementationType == typeof(AutorizacionConsultaRiesgoDurable)).ToList();

        Assert.Single(durable);
        Assert.Equal(typeof(AutorizacionConsultaRiesgoDurable), durable[0].ServiceType);
        Assert.DoesNotContain(services, d =>
            d.ServiceType == typeof(IConsultaRiesgoAutorizacion)
            && d.ImplementationType == typeof(AutorizacionConsultaRiesgoDurable));
    }

    [Fact]
    public async Task Resuelve_QaGate_con_durable_interna_y_sin_config_no_autoriza()
    {
        var services = Wired();
        // XPAY-717: el gate QA sólo se cablea en ambiente no productivo.
        services.AddSingleton(AmbienteXpayTests.Ambiente("QA"));
        services.AddSingleton<IConfiguration>(new FakeConfiguration(new Dictionary<string, string?>()));
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        // Cadena ficticia: el contexto se construye pero nunca abre conexión.
        services.AddDbContext<XpayDbContext>(o =>
            o.UseSqlServer("Server=xpay656.invalid;Database=none;Encrypt=false;Connect Timeout=1"));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var autz = scope.ServiceProvider.GetRequiredService<IConsultaRiesgoAutorizacion>();

        var gate = Assert.IsType<AutorizacionConsultaRiesgoQaGate>(autz);
        Assert.IsType<AutorizacionConsultaRiesgoDurable>(gate.AutorizacionDurable);
        Assert.False(await gate.TieneAutorizacionVigenteAsync(1, 1));
        Assert.False(await gate.TieneAutorizacionVigenteAsync(999_999, 424_242));

        // El wiring de riesgo no registra el cliente MiDecisor: la barrera de
        // autorización no tiene camino al proveedor.
        Assert.Null(scope.ServiceProvider.GetService<IMiDecisorClient>());
    }

    // ── XPAY-717 — frontera QA/PROD en el wiring ──────────────────────────
    // Ambiente productivo (o sin autoridad registrada) → gate productivo con la
    // durable concreta interna. Aunque estén TODAS las variables QA, sin
    // MIDECISOR_PRODUCTION_ENABLED no autoriza y no abre la BD.
    private static ServiceProvider ProveedorConVariablesQa(IAmbienteXpay? ambiente)
    {
        var services = Wired();
        if (ambiente is not null)
            services.AddSingleton(ambiente);
        services.AddSingleton<IConfiguration>(new FakeConfiguration(new Dictionary<string, string?>
        {
            [MiDecisorQaGateConfig.EnvAllowedSolicitudId] = "1",
            [MiDecisorQaGateConfig.EnvStopAfterConsulta]  = "true",
            [MiDecisorQaGateConfig.EnvGateMode]           = MiDecisorQaGateConfig.ModoClienteQaFullFlow,
        }));
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        // Cadena ficticia: el contexto se construye pero nunca abre conexión.
        services.AddDbContext<XpayDbContext>(o =>
            o.UseSqlServer("Server=xpay717.invalid;Database=none;Encrypt=false;Connect Timeout=1"));
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("qa")]
    public async Task Ambiente_productivo_resuelve_gate_productivo_y_variables_QA_no_autorizan(string nombre)
    {
        await using var provider = ProveedorConVariablesQa(AmbienteXpayTests.Ambiente(nombre));
        await using var scope = provider.CreateAsyncScope();

        var gate = Assert.IsType<AutorizacionConsultaRiesgoProduccion>(
            scope.ServiceProvider.GetRequiredService<IConsultaRiesgoAutorizacion>());

        Assert.IsType<AutorizacionConsultaRiesgoDurable>(gate.AutorizacionDurable);
        Assert.False(await gate.TieneAutorizacionVigenteAsync(1, 1));
    }

    [Fact]
    public async Task Sin_autoridad_de_ambiente_resuelve_gate_productivo_fail_closed()
    {
        await using var provider = ProveedorConVariablesQa(ambiente: null);
        await using var scope = provider.CreateAsyncScope();

        var gate = Assert.IsType<AutorizacionConsultaRiesgoProduccion>(
            scope.ServiceProvider.GetRequiredService<IConsultaRiesgoAutorizacion>());

        Assert.False(await gate.TieneAutorizacionVigenteAsync(1, 1));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public async Task Ambiente_no_productivo_conserva_gate_QA(string nombre)
    {
        await using var provider = ProveedorConVariablesQa(AmbienteXpayTests.Ambiente(nombre));
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<AutorizacionConsultaRiesgoQaGate>(
            scope.ServiceProvider.GetRequiredService<IConsultaRiesgoAutorizacion>());
    }

    [Fact]
    public void Store_productivo_registrado_scoped()
    {
        var store = Assert.Single(Wired(), d => d.ServiceType == typeof(IRiesgoProduccionStore));

        Assert.Equal(typeof(RiesgoProduccionStore), store.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, store.Lifetime);
    }

    [Fact]
    public async Task Stub_devuelve_false_siempre()
    {
        var stub = new AutorizacionConsultaRiesgoNoDisponible();

        Assert.False(await stub.TieneAutorizacionVigenteAsync(1, 1));
        Assert.False(await stub.TieneAutorizacionVigenteAsync(999_999, 424_242));
    }

    [Fact]
    public void Primitivas_downstream_y_orquestador_quedan_registrados()
    {
        var services = Wired();

        Assert.Contains(services, d => d.ServiceType == typeof(ICarteraResultadoRiesgoConsumo));
        Assert.Contains(services, d => d.ServiceType == typeof(ICarteraDecisionCrediticia));
        Assert.Contains(services, d => d.ServiceType == typeof(ICarteraMaterializacionCupo));
        Assert.Contains(services, d => d.ServiceType == typeof(ICarteraAutorizacionConsultaRiesgoStore));
        Assert.Contains(services, d => d.ServiceType == typeof(ICarteraSolicitudEvaluacionReader));
        Assert.Contains(services, d => d.ServiceType == typeof(ICarteraDecisionCrediticiaOrchestrator));
        Assert.Contains(services, d => d.ServiceType == typeof(ICarteraConsultaRiesgoReconciliacion));
    }
}
