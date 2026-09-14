using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-312 — verificación de PRESENCIA de configuración Passport sin exponer
// valores. Lee EXCLUSIVAMENTE desde IConfiguration construido a partir de
// variables de entorno del proceso (AddEnvironmentVariables en Program.cs) —
// este código NUNCA abre ni parsea ~/.passport-sandbox.env directamente
// (XPAY-312 FASE 4). Reutiliza los nombres EXACTOS ya definidos en
// PassportOptions (EnvBaseUrl/EnvClientId/EnvClientSecret) — no inventa
// nombres nuevos.
public sealed record HarnessConfigStatus(
    bool BaseUrlPresent,
    bool ApiKeyPresent,
    bool ApiSecretPresent)
{
    public bool AllPresent => BaseUrlPresent && ApiKeyPresent && ApiSecretPresent;

    public static HarnessConfigStatus FromConfiguration(IConfiguration configuration) => new(
        BaseUrlPresent:   !string.IsNullOrWhiteSpace(configuration[PassportOptions.EnvBaseUrl]),
        ApiKeyPresent:    !string.IsNullOrWhiteSpace(configuration[PassportOptions.EnvClientId]),
        ApiSecretPresent: !string.IsNullOrWhiteSpace(configuration[PassportOptions.EnvClientSecret]));

    // Único formato de impresión permitido para esta información — jamás el
    // valor, ni longitud, ni prefijo/sufijo (XPAY-312 FASE 4).
    public IEnumerable<string> ToRedactedLines()
    {
        yield return $"{PassportOptions.EnvBaseUrl}={(BaseUrlPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{PassportOptions.EnvClientId}={(ApiKeyPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{PassportOptions.EnvClientSecret}={(ApiSecretPresent ? "AVAILABLE" : "MISSING")}";
    }
}
