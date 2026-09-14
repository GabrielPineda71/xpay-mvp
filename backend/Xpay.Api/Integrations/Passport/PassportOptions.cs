namespace Xpay.Api.Integrations.Passport;

// XPAY-272 — sólo forma de configuración para el cliente HTTP base y el
// proveedor OAuth de Passport (Bre-B como Servicio). NO contiene valores,
// NO defaults de credenciales, NO URL de ambiente. Los secretos se leen del
// entorno (Azure App Settings), nunca de appsettings.json ni del repo — mismo
// patrón que Veriff (KycService) y MiDecisor.
//
// Nombres de entorno: se REUTILIZAN los ya existentes en este mismo proyecto
// para Passport (BrebService.GetHealthConfig; docs/PASSPORT_BREB_PLAN.md
// §8/§18): PASSPORT_BASE_URL / PASSPORT_API_KEY / PASSPORT_API_SECRET.
// NO se reutiliza ningún nombre de MiDecisor (MIDECISOR_*) ni se inventan
// nombres nuevos que dupliquen la configuración Passport ya documentada.
//
// docs/PASSPORT_BREB_PLAN.md §8 documenta "https://api.passportfintech.com
// (sandbox URL a confirmar)" como valor de EJEMPLO histórico, no confirmado
// contractualmente (ver docs/GOVERNANCE... análisis Passport XPAY-271,
// STALE_INTERNAL_DOCUMENTATION). NO se usa como default aquí. Sin BaseUrl
// configurada, la operación falla closed (ver PassportTokenProvider /
// PassportHttpClient) ANTES de cualquier llamada HTTP — nunca se inventa
// un valor por defecto para un dato de ambiente real.
public sealed class PassportOptions
{
    public const string EnvBaseUrl      = "PASSPORT_BASE_URL";
    public const string EnvClientId     = "PASSPORT_API_KEY";
    public const string EnvClientSecret = "PASSPORT_API_SECRET";

    // Config NO secreta, sólo detalle técnico de transporte/cache — no forma
    // parte del contrato de Passport.
    public const string EnvTimeoutSeconds           = "PASSPORT_TIMEOUT_SECONDS";
    public const string EnvTokenSafetyMarginSeconds = "PASSPORT_TOKEN_SAFETY_MARGIN_SECONDS";

    // Ruta del endpoint de token OAuth2 — contrato confirmado y sin
    // ambigüedad (a diferencia de la ruta de consulta de MiDecisor, aquí no
    // hay una elección `/client` vs `/pn` pendiente). NO es configurable por
    // entorno: es una constante del contrato.
    public const string OAuthTokenPath = "/v1/iam/oauth/tokens";

    public const int DefaultTimeoutSeconds           = 30;
    public const int DefaultTokenSafetyMarginSeconds = 60;

    public string? BaseUrl      { get; set; }
    public string? ClientId     { get; set; }
    public string? ClientSecret { get; set; }

    public int TimeoutSeconds           { get; set; } = DefaultTimeoutSeconds;
    public int TokenSafetyMarginSeconds { get; set; } = DefaultTokenSafetyMarginSeconds;

    // Presencia de config sin exponer valores (mismo criterio que
    // MiDecisorOptions.TieneConfiguracionCompleta / KycService con Veriff).
    // Reutilizado ya hoy por BrebService.GetHealthConfig vía IConfiguration
    // directa — esta propiedad es para los nuevos consumidores tipados.
    public bool TieneConfiguracionCompleta =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    public static PassportOptions FromConfiguration(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        out IReadOnlyList<string> numericWarnings)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var warnings = new List<string>();
        var opts = new PassportOptions
        {
            BaseUrl      = configuration[EnvBaseUrl],
            ClientId     = configuration[EnvClientId],
            ClientSecret = configuration[EnvClientSecret],
        };

        opts.TimeoutSeconds =
            ParseBoundedIntOrDefault(configuration[EnvTimeoutSeconds], DefaultTimeoutSeconds, EnvTimeoutSeconds, minInclusive: 1, warnings);
        opts.TokenSafetyMarginSeconds =
            ParseBoundedIntOrDefault(configuration[EnvTokenSafetyMarginSeconds], DefaultTokenSafetyMarginSeconds, EnvTokenSafetyMarginSeconds, minInclusive: 0, warnings);

        numericWarnings = warnings;
        return opts;
    }

    private static int ParseBoundedIntOrDefault(string? raw, int fallback, string key, int minInclusive, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (int.TryParse(raw.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= minInclusive)
            return value;

        warnings.Add(key);
        return fallback;
    }
}
