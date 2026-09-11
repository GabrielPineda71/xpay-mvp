using Microsoft.Extensions.DependencyInjection;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Services;

namespace Xpay.Api.Common;

// M2.4d — registro DI del pipeline de riesgo/decisión de Cartera Ordinaria.
//
// CRÍTICO — NO ACTIVACIÓN: IConsultaRiesgoAutorizacion permanece ligada al stub
// fail-closed AutorizacionConsultaRiesgoNoDisponible (devuelve false siempre).
// Mientras esa línea no cambie, ningún endpoint puede alcanzar una llamada real
// a MiDecisor: el pre-flight de CarteraConsultaRiesgoService aborta antes de
// TX-A y antes de cualquier HTTP. Registrar las primitivas downstream (consumo
// M2.4a / decisión M2.4b / materialización M2.4c) NO equivale a autorizar el
// proveedor — sólo permite que el orquestador compile y sea probado.
//
// La activación real (swap del stub por AutorizacionConsultaRiesgoDurable) es
// una decisión separada, fuera del alcance de XPAY-204.
public static class CarteraRiesgoRuntimeWiring
{
    public static IServiceCollection AddCarteraRiesgoRuntime(this IServiceCollection services)
    {
        // Orquestación estructural Cartera ↔ MiDecisor (M2.3a/b1).
        services.AddScoped<CarteraConsultaRiesgoService>();
        services.AddScoped<ICarteraConsultaRiesgoStore, CarteraConsultaRiesgoStore>();

        // BARRERA DE AUTORIZACIÓN — STUB FAIL-CLOSED. NO reemplazar aquí.
        services.AddScoped<IConsultaRiesgoAutorizacion, AutorizacionConsultaRiesgoNoDisponible>();

        // Primitivas downstream del pipeline (M2.4a/b/c) — antes DORMIDAS (0 DI).
        // Se registran para el orquestador M2.4d. Cada una gestiona su propia
        // transacción/AppLock; una instancia scoped independiente es inofensiva.
        services.AddScoped<ICarteraResultadoRiesgoConsumo, CarteraConsultaRiesgoStore>();
        services.AddScoped<ICarteraDecisionCrediticia, CarteraDecisionCrediticiaStore>();
        services.AddScoped<ICarteraMaterializacionCupo, CarteraMaterializacionCupoStore>();
        services.AddScoped<ICarteraAutorizacionConsultaRiesgoStore, CarteraAutorizacionConsultaRiesgoStore>();

        // Lectura de estado + orquestador + reconciliación fail-closed (M2.4d).
        services.AddScoped<ICarteraSolicitudEvaluacionReader, CarteraSolicitudEvaluacionReader>();
        services.AddScoped<ICarteraDecisionCrediticiaOrchestrator, CarteraDecisionCrediticiaOrchestrator>();
        services.AddScoped<ICarteraConsultaRiesgoReconciliacion, CarteraConsultaRiesgoReconciliacionStore>();

        // XPAY-213/214 — purge B4 (política de retención, XPAY-212 §Q.8): sólo
        // el store de purga + el batch runner reutilizable, para que el
        // endpoint admin manual pueda resolverse. NO se registra ningún
        // scheduler/IHostedService — la ejecución periódica es una fase
        // posterior separada, no autorizada aquí. Esto NO activa ninguna
        // purga real: el batch runner y el endpoint quedan implementados pero
        // sin invocarse en este prompt.
        services.AddScoped<ICarteraResultadoRiesgoPurga, CarteraConsultaRiesgoStore>();
        services.AddScoped<ICarteraConsultaRiesgoPurgaBatchRunner, CarteraConsultaRiesgoPurgaBatchRunner>();

        return services;
    }
}
