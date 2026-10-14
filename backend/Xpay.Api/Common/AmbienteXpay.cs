using Microsoft.Extensions.Hosting;

namespace Xpay.Api.Common;

// XPAY-717 — frontera central QA/PROD.
//
// Los mecanismos de simulación, bypass, diagnóstico y operaciones QA sólo
// pueden funcionar si ASPNETCORE_ENVIRONMENT es EXACTAMENTE uno de los
// ambientes no productivos de la lista cerrada (comparación ordinal,
// sensible a mayúsculas): Development, QA, Test.
//
// Cualquier otro valor es PRODUCTIVO (fail-closed): Production, Staging,
// vacío/default, "qa" en minúsculas, typos o valores desconocidos.
//
// La guardia se evalúa SIEMPRE antes que cualquier flag de configuración: no
// existe ninguna variable capaz de reactivar un mecanismo QA en un ambiente
// productivo. La configuración QA (XPAY_QA_*, MIDECISOR_QA_*, etc.) sólo
// puede RESTRINGIR adicionalmente dentro de un ambiente no productivo.
public interface IAmbienteXpay
{
    // Nombre de ambiente tal como lo reporta el host (para logs/diagnóstico).
    string Nombre { get; }

    // true SÓLO en Development, QA o Test (exacto).
    bool PermiteMecanismosNoProductivos { get; }

    // Negación estricta de PermiteMecanismosNoProductivos.
    bool EsProductivo { get; }
}

public sealed class AmbienteXpay(IHostEnvironment environment) : IAmbienteXpay
{
    public const string Development = "Development";
    public const string Qa          = "QA";
    public const string Test        = "Test";

    public string Nombre => environment.EnvironmentName ?? string.Empty;

    public bool PermiteMecanismosNoProductivos => EsNoProductivo(environment.EnvironmentName);

    public bool EsProductivo => !PermiteMecanismosNoProductivos;

    // Regla única reutilizable (también por Program.cs antes de construir DI).
    public static bool EsNoProductivo(string? environmentName) =>
        string.Equals(environmentName, Development, StringComparison.Ordinal)
        || string.Equals(environmentName, Qa, StringComparison.Ordinal)
        || string.Equals(environmentName, Test, StringComparison.Ordinal);
}
