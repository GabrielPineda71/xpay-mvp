namespace Xpay.Api.Integrations.Passport;

// XPAY-385 — clasifica si PASSPORT_BASE_URL apunta al host Sandbox
// conocido de Passport o no. Función PURA — sin I/O, sin llamadas Passport,
// 100% testeable offline.
//
// El host Sandbox ("api.paas.sandbox.co.passportfintech.com") ya es una
// constante conocida y usada en este mismo repositorio para el mismo
// propósito (ver tools/Xpay.PassportSandboxHarness/SandboxHostGuard.cs,
// XPAY-3xx) — se duplica deliberadamente aquí en vez de referenciarla
// desde el harness: Xpay.Api es la librería productiva y NUNCA depende
// del harness (la dependencia va en sentido contrario), mismo criterio ya
// aplicado a Fingerprint.Compute/BrebService.ComputeKeyHash.
//
// FAIL-SAFE deliberado: cualquier host NO reconocido explícitamente como
// el Sandbox conocido se clasifica como PRODUCCION, nunca al revés — es
// preferible tratar por error una cuenta real como si necesitara la misma
// cautela que producción, que etiquetar por error una cuenta de
// producción real como "Sandbox" (lo que podría llevar a tratarla con
// menos cuidado del debido en logs/UI/pruebas).
public static class PassportEnvironmentClassifier
{
    public const string SandboxHost      = "api.paas.sandbox.co.passportfintech.com";
    public const string AmbienteSandbox   = "SANDBOX";
    public const string AmbienteProduccion = "PRODUCCION";

    public static string Clasificar(string? baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl)
            && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, SandboxHost, StringComparison.OrdinalIgnoreCase))
        {
            return AmbienteSandbox;
        }

        return AmbienteProduccion;
    }
}
