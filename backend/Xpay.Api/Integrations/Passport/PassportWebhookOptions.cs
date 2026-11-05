using Microsoft.Extensions.Configuration;

namespace Xpay.Api.Integrations.Passport;

// XPAY-594 — configuración NO secreta del webhook entrante de Passport.
// Mismo patrón que PassportOptions/MiDecisorOptions: los secretos se leen del
// entorno (Azure App Settings), NUNCA de appsettings.json ni del repositorio.
public sealed class PassportWebhookOptions
{
    public const string EnvSecret                    = "PASSPORT_WEBHOOK_SECRET";
    public const string EnvSignatureMode             = "PASSPORT_WEBHOOK_SIGNATURE_MODE";
    public const string EnvTimestampToleranceSeconds = "PASSPORT_WEBHOOK_TIMESTAMP_TOLERANCE_SECONDS";

    /// <summary>
    /// Cabecera de firma V2 documentada por Passport.
    /// </summary>
    public const string HeaderSignature = "X-Passport-Signature-V2";

    /// <summary>
    /// Cabecera de marca de tiempo que entra en el material firmado.
    /// </summary>
    public const string HeaderTimestamp = "X-Passport-Timestamp";

    /// <summary>
    /// Tolerancia por defecto de la ventana anti-reenvío.
    ///
    /// TIMESTAMP_TOLERANCE_SOURCE = XPAY_CONFIGURABLE_DEFAULT. La documentación
    /// de Passport NO fija ningún valor: sólo indica que V2 no incluye nonce y
    /// que debe revisarse el timestamp para descartar reenvíos. 300 s es una
    /// elección conservadora de XPAY, configurable, y debe declararse como tal
    /// en cualquier evidencia — no atribuirla al proveedor.
    /// </summary>
    public const int DefaultTimestampToleranceSeconds = 300;

    /// <summary>
    /// ENFORCE rechaza toda entrega cuya firma no valide. Es el valor por
    /// defecto: un modo permisivo nunca debe obtenerse por omisión.
    /// </summary>
    public const string ModeEnforce = "ENFORCE";

    /// <summary>
    /// OBSERVE registra el resultado de la verificación sin rechazar. Sólo
    /// admisible en Sandbox, donde la documentación NO confirma que
    /// X-Passport-Signature-V2 se envíe, y debe quedar declarado en la
    /// evidencia cuando se use.
    /// </summary>
    public const string ModeObserve = "OBSERVE";

    public string? Secret { get; init; }
    public string  Mode   { get; init; } = ModeEnforce;
    public int     TimestampToleranceSeconds { get; init; } = DefaultTimestampToleranceSeconds;

    public bool IsEnforcing => !string.Equals(Mode, ModeObserve, StringComparison.OrdinalIgnoreCase);

    public static PassportWebhookOptions FromConfiguration(IConfiguration configuration)
    {
        var rawMode = configuration[EnvSignatureMode]?.Trim();

        // Fail-closed: cualquier valor no reconocido cae en ENFORCE.
        var mode = string.Equals(rawMode, ModeObserve, StringComparison.OrdinalIgnoreCase)
            ? ModeObserve
            : ModeEnforce;

        var tolerance = DefaultTimestampToleranceSeconds;
        var rawTolerance = configuration[EnvTimestampToleranceSeconds];
        if (!string.IsNullOrWhiteSpace(rawTolerance)
            && int.TryParse(rawTolerance.Trim(), out var parsed)
            && parsed >= 0)
        {
            tolerance = parsed;
        }

        return new PassportWebhookOptions
        {
            // Trim: evita que un espacio final en Azure App Settings rompa el
            // HMAC — misma defensa ya aplicada en KycService.
            Secret = configuration[EnvSecret]?.Trim(),
            Mode = mode,
            TimestampToleranceSeconds = tolerance,
        };
    }
}
