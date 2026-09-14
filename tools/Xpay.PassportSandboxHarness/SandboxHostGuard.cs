namespace Xpay.PassportSandboxHarness;

// XPAY-312 — guard de seguridad: impide que el harness apunte a un host que
// no sea EXACTAMENTE el Sandbox autorizado de Passport, sin importar qué
// valor tenga PASSPORT_BASE_URL en el entorno del proceso. No existe ningún
// flag para saltarse este guard (p. ej. un hipotético --force-prod): NO se
// implementa, deliberadamente, para no soportar producción desde este
// harness bajo ninguna circunstancia.
public static class SandboxHostGuard
{
    // Host confirmado en vivo contra docs.passportfintech.com (XPAY-310).
    public const string AuthorizedSandboxHost = "api.paas.sandbox.co.passportfintech.com";

    public static bool IsAuthorizedSandboxHost(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        return string.Equals(uri.Host, AuthorizedSandboxHost, StringComparison.OrdinalIgnoreCase);
    }
}
