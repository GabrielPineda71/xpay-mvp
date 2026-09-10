using Microsoft.Extensions.DependencyInjection;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// M2.4d — el wiring de runtime registra el pipeline PERO NO ACTIVA el proveedor:
// IConsultaRiesgoAutorizacion sigue ligada al stub fail-closed
// AutorizacionConsultaRiesgoNoDisponible. Se inspeccionan los ServiceDescriptor
// (no se construye el contenedor: no hacen falta dependencias reales).
public sealed class CarteraRiesgoRuntimeWiringTests
{
    private static ServiceCollection Wired()
    {
        var services = new ServiceCollection();
        services.AddCarteraRiesgoRuntime();
        return services;
    }

    // 13/14 ─────────────────────────────────────────────────────────────
    [Fact]
    public void Autorizacion_sigue_siendo_el_stub_NoDisponible()
    {
        var services = Wired();

        var autz = services.Where(d => d.ServiceType == typeof(IConsultaRiesgoAutorizacion)).ToList();

        Assert.Single(autz);
        Assert.Equal(typeof(AutorizacionConsultaRiesgoNoDisponible), autz[0].ImplementationType);
    }

    [Fact]
    public void Durable_NO_queda_registrado_como_IConsultaRiesgoAutorizacion()
    {
        var services = Wired();

        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(AutorizacionConsultaRiesgoDurable));
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
