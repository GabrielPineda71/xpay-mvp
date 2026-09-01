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
public sealed class MiDecisorOptions
{
    public const string EnvBaseUrl      = "MIDECISOR_BASE_URL";
    public const string EnvClientId     = "MIDECISOR_CLIENT_ID";
    public const string EnvClientSecret = "MIDECISOR_CLIENT_SECRET";
    public const string EnvUsername     = "MIDECISOR_USERNAME";
    public const string EnvPassword     = "MIDECISOR_PASSWORD";

    public string? BaseUrl      { get; set; }
    public string? ClientId     { get; set; }
    public string? ClientSecret { get; set; }
    public string? Username     { get; set; }
    public string? Password     { get; set; }

    // Presencia de config sin exponer valores (para un check "hasConfig" en M2,
    // mismo criterio que KycService con Veriff). No loguear los valores.
    public bool TieneConfiguracionCompleta =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);
}
