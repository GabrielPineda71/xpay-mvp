namespace Xpay.Api.Integrations.MiDecisor;

// M1 — sólo forma de configuración. NO contiene valores, NO defaults de
// credenciales, NO URLs de ambiente. La resolución real (env vars / Azure
// App Settings) y la autenticación se implementan en M2.
//
// Patrón del proyecto (igual que Veriff en KycService): los secretos del
// proveedor se leen del entorno, NUNCA de appsettings.json ni del repo.
// Claves de entorno previstas:
//
//   MIDECISOR_BASE_URL       — URL base del ambiente asignado a XPAY
//                              (dev/qa/test/demo/prod -api.datacredito.com.co).
//                              Ambiente y URL quedan por confirmar (bloqueador 037).
//   MIDECISOR_CLIENT_ID      — header Client_id del endpoint de token OAuth2.
//   MIDECISOR_CLIENT_SECRET  — header Client_secret del endpoint de token OAuth2.
//   MIDECISOR_USERNAME       — campo "username" del body del token.
//   MIDECISOR_PASSWORD       — campo "password" del body del token.
//
// Titularidad de las credenciales (XPAY vs. DAFIN/Xelecredit): UNRESOLVED
// (bloqueador 037). No reutilizar credenciales históricas de otro proyecto.
//
// M2.1 añade config NO secreta del token provider (auth path, timeout,
// safety margin). Sólo el auth path tiene default (es la ruta del contrato
// oficial); base URL y credenciales NO tienen default y su ausencia hace
// fail-closed cuando se invoca GetAccessTokenAsync (nunca en el arranque).
public sealed class MiDecisorOptions
{
    public const string EnvBaseUrl      = "MIDECISOR_BASE_URL";
    public const string EnvClientId     = "MIDECISOR_CLIENT_ID";
    public const string EnvClientSecret = "MIDECISOR_CLIENT_SECRET";
    public const string EnvUsername     = "MIDECISOR_USERNAME";
    public const string EnvPassword     = "MIDECISOR_PASSWORD";

    // M2.1 — config NO secreta.
    public const string EnvAuthPath                 = "MIDECISOR_AUTH_PATH";
    public const string EnvTimeoutSeconds           = "MIDECISOR_TIMEOUT_SECONDS";
    public const string EnvTokenSafetyMarginSeconds = "MIDECISOR_TOKEN_SAFETY_MARGIN_SECONDS";

    // Defaults estructurales (no son secretos, no son URLs de ambiente).
    public const string DefaultAuthPath                 = "/spla/oauth2/v1/token";
    public const int    DefaultTimeoutSeconds           = 30;
    public const int    DefaultTokenSafetyMarginSeconds = 30;

    public string? BaseUrl      { get; set; }
    public string? ClientId     { get; set; }
    public string? ClientSecret { get; set; }
    public string? Username     { get; set; }
    public string? Password     { get; set; }

    // Ruta del endpoint de token OAuth2, relativa a BaseUrl. Default = ruta
    // del contrato oficial confirmado.
    public string AuthPath { get; set; } = DefaultAuthPath;

    // Timeout de transporte para la llamada de auth (segundos).
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    // Margen de seguridad restado a expires_in antes de considerar el token
    // caducado en cache (segundos).
    public int TokenSafetyMarginSeconds { get; set; } = DefaultTokenSafetyMarginSeconds;

    // Presencia de config sin exponer valores (para un check "hasConfig" en M2,
    // mismo criterio que KycService con Veriff). No loguear los valores.
    public bool TieneConfiguracionCompleta =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);

    // Construye las opciones desde configuración plana (mismo patrón que
    // KycService con las claves VERIFF_*: se leen del entorno / IConfiguration,
    // NUNCA de appsettings.json). Parseo defensivo de los valores numéricos —
    // ante valor fuera de rango se usa el default estructural y `numericWarnings`
    // recibe el nombre de la clave para un log saneado:
    //   TimeoutSeconds            — requiere > 0 (un timeout de 0 s no tiene sentido).
    //   TokenSafetyMarginSeconds  — acepta >= 0 (0 = usar el token hasta su
    //                               expiración exacta, opción legítima del operador);
    //                               sólo un valor negativo cae al default.
    public static MiDecisorOptions FromConfiguration(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        out IReadOnlyList<string> numericWarnings)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var warnings = new List<string>();
        var opts = new MiDecisorOptions
        {
            BaseUrl      = configuration[EnvBaseUrl],
            ClientId     = configuration[EnvClientId],
            ClientSecret = configuration[EnvClientSecret],
            Username     = configuration[EnvUsername],
            Password     = configuration[EnvPassword],
        };

        var authPath = configuration[EnvAuthPath];
        opts.AuthPath = string.IsNullOrWhiteSpace(authPath) ? DefaultAuthPath : authPath.Trim();

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
