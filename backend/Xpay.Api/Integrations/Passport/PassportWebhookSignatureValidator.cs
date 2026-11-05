using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Xpay.Api.Integrations.Passport;

// XPAY-594 — implementación de la verificación "X-Passport-Signature-V2".
//
// Material firmado, según el contrato cerrado en XPAY-593:
//
//     HMAC-SHA256( secret , "<X-Passport-Timestamp>.<JSON_CANONICO>" )
//
// El timestamp se usa VERBATIM tal como llegó en la cabecera: no se
// re-formatea, porque cualquier normalización cambiaría el material firmado.
// Sólo se interpreta —aparte— para aplicar la ventana anti-reenvío.
//
// La codificación del valor de la cabecera de firma (hex o base64) NO está
// documentada por Passport. Se aceptan ambas, siempre con comparación en tiempo
// constante; no hay riesgo en ello porque un atacante debe acertar el HMAC
// completo en cualquiera de las dos codificaciones.
//
// Nunca se registra el secreto, la firma, el material HMAC ni el cuerpo crudo:
// sólo longitudes, formato y resultado.
public sealed class PassportWebhookSignatureValidator : IPassportWebhookSignatureValidator
{
    private readonly IConfiguration _configuration;
    private readonly TimeProvider   _timeProvider;
    private readonly ILogger<PassportWebhookSignatureValidator> _logger;

    public PassportWebhookSignatureValidator(
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<PassportWebhookSignatureValidator> logger)
    {
        _configuration = configuration;
        _timeProvider  = timeProvider;
        _logger        = logger;
    }

    public PassportWebhookSignatureResult Validate(
        string rawBody, string? signatureHeader, string? timestampHeader)
    {
        var options = PassportWebhookOptions.FromConfiguration(_configuration);
        var enforcing = options.IsEnforcing;

        PassportWebhookSignatureResult Result(PassportWebhookSignatureOutcome outcome, bool valid)
        {
            var result = new PassportWebhookSignatureResult(outcome, valid, enforcing && !valid);
            _logger.LogInformation(
                "Passport webhook signature check: outcome={Outcome} valid={Valid} enforcing={Enforcing} " +
                "sigLen={SigLen} tsLen={TsLen} bodyLen={BodyLen}",
                outcome, valid, enforcing,
                signatureHeader?.Trim().Length ?? 0,
                timestampHeader?.Trim().Length ?? 0,
                rawBody.Length);
            return result;
        }

        if (string.IsNullOrWhiteSpace(options.Secret))
            return Result(PassportWebhookSignatureOutcome.SecretNotConfigured, false);

        if (string.IsNullOrWhiteSpace(signatureHeader))
            return Result(PassportWebhookSignatureOutcome.SignatureMissing, false);

        if (string.IsNullOrWhiteSpace(timestampHeader))
            return Result(PassportWebhookSignatureOutcome.TimestampMissing, false);

        var timestamp = timestampHeader.Trim();

        if (!TryParseTimestamp(timestamp, out var sentAtUtc))
            return Result(PassportWebhookSignatureOutcome.TimestampUnparseable, false);

        var skew = (_timeProvider.GetUtcNow() - sentAtUtc).Duration();
        if (skew > TimeSpan.FromSeconds(options.TimestampToleranceSeconds))
            return Result(PassportWebhookSignatureOutcome.TimestampOutOfWindow, false);

        string canonical;
        try
        {
            canonical = PassportWebhookCanonicalJson.Canonicalize(rawBody);
        }
        catch (JsonException)
        {
            // Un cuerpo no parseable no puede producir la forma canónica que
            // Passport firmó, así que la firma no puede ser válida.
            return Result(PassportWebhookSignatureOutcome.SignatureInvalid, false);
        }

        var message  = $"{timestamp}.{canonical}";
        var keyBytes = Encoding.UTF8.GetBytes(options.Secret!);

        using var hmac = new HMACSHA256(keyBytes);
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));

        return SignatureMatches(computed, signatureHeader.Trim())
            ? Result(PassportWebhookSignatureOutcome.Valid, true)
            : Result(PassportWebhookSignatureOutcome.SignatureInvalid, false);
    }

    /// <summary>
    /// Compara en tiempo constante contra la firma recibida, aceptando hex o
    /// base64. Nunca revela ni registra el valor comparado.
    /// </summary>
    private static bool SignatureMatches(byte[] computed, string provided)
    {
        if (TryDecodeHex(provided, out var hexBytes)
            && CryptographicOperations.FixedTimeEquals(computed, hexBytes))
            return true;

        try
        {
            var b64 = Convert.FromBase64String(provided);
            return CryptographicOperations.FixedTimeEquals(computed, b64);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryDecodeHex(string s, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (s.Length % 2 != 0) return false;
        var buffer = new byte[s.Length / 2];
        for (var i = 0; i < buffer.Length; i++)
        {
            if (!byte.TryParse(
                    s.AsSpan(i * 2, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out buffer[i]))
                return false;
        }
        bytes = buffer;
        return true;
    }

    /// <summary>
    /// Interpreta el timestamp SÓLO para la ventana anti-reenvío. Passport no
    /// documenta su formato, así que se aceptan epoch en segundos, epoch en
    /// milisegundos e ISO-8601.
    /// </summary>
    internal static bool TryParseTimestamp(string raw, out DateTimeOffset utc)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            // Umbral clásico: por encima de ~1e12 el valor está en milisegundos.
            utc = epoch > 99_999_999_999L
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch);
            return true;
        }

        if (DateTimeOffset.TryParse(
                raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
            return true;

        utc = default;
        return false;
    }
}
