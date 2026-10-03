using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Services;

namespace Xpay.Api.Common;

// M2.4d — registro DI del pipeline de riesgo/decisión de Cartera Ordinaria.
//
// CRÍTICO — ACTIVACIÓN QA ACOTADA (XPAY-656): IConsultaRiesgoAutorizacion se
// liga a AutorizacionConsultaRiesgoQaGate, que decora la autorización durable
// AutorizacionConsultaRiesgoDurable. El gate autoriza EXCLUSIVAMENTE la única
// solicitud configurada en MIDECISOR_QA_ALLOWED_SOLICITUD_ID, sólo si
// MIDECISOR_STOP_AFTER_CONSULTA vale exactamente "true" y sólo si el
// consentimiento durable V1 es válido (sigue siendo obligatorio). Sin esa
// configuración el gate devuelve false siempre (fail-closed, mismo efecto que el
// stub AutorizacionConsultaRiesgoNoDisponible, que se conserva pero ya no se
// registra): el pre-flight de CarteraConsultaRiesgoService aborta antes de TX-A
// y antes de cualquier HTTP. Registrar las primitivas downstream (consumo M2.4a /
// decisión M2.4b / materialización M2.4c) NO equivale a autorizar el proveedor.
//
// Esto NO es la activación general de MiDecisor: habilitar consultas sin
// allowlist sigue siendo una decisión separada.
public static class CarteraRiesgoRuntimeWiring
{
    public static IServiceCollection AddCarteraRiesgoRuntime(this IServiceCollection services)
    {
        // Orquestación estructural Cartera ↔ MiDecisor (M2.3a/b1).
        services.AddScoped<CarteraConsultaRiesgoService>();
        services.AddScoped<ICarteraConsultaRiesgoStore, CarteraConsultaRiesgoStore>();

        // BARRERA DE AUTORIZACIÓN — gate QA fail-closed (XPAY-656). La durable
        // se registra SÓLO como tipo concreto (no como IConsultaRiesgoAutorizacion)
        // y la factory la pasa explícitamente como dependencia interna: el gate
        // nunca se resuelve a sí mismo.
        services.AddScoped<AutorizacionConsultaRiesgoDurable>();
        services.AddScoped<IConsultaRiesgoAutorizacion>(sp => new AutorizacionConsultaRiesgoQaGate(
            sp.GetRequiredService<AutorizacionConsultaRiesgoDurable>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<AutorizacionConsultaRiesgoQaGate>>()));

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
