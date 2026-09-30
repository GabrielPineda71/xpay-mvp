using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Xpay.Api.Integrations.Passport;

/// <summary>
/// Proyección MÍNIMA y SANITIZADA de una entrega de webhook de Passport —
/// sólo lo que M5-T6/T7 necesita para recibir y validar.
///
/// Los bloques <c>sender</c> y <c>receiver</c> del payload original contienen
/// PII y datos bancarios (nombres, tipo y número de identificación, tipo y
/// número de cuenta) y NUNCA se proyectan ni se persisten.
/// </summary>
public sealed record PassportWebhookPayload(
    string?                  EventName,
    PassportInboundEventKind Kind,
    string?                  PaymentId,
    string?                  Status,
    string?                  AccountId,
    string?                  CreatedAt,
    string?                  UpdatedAt,
    string?                  SettledAt,
    string?                  DestinationKeyType,
    bool                     DestinationKeyPresent,
    string?                  QrCodeReference,
    string?                  EndToEndIdentification,
    string?                  ErrorCode,
    string?                  ErrorDescription,
    string?                  AmountValue,
    string?                  AmountCurrency)
{
    public bool HasPaymentId => !string.IsNullOrWhiteSpace(PaymentId);

    /// <summary>
    /// Verdadero si la entrega trae lo mínimo para ser tratada: un evento
    /// entrante reconocido y un <c>payment_id</c>. Es la validación de T7.
    /// </summary>
    public bool IsProcessableInbound =>
        PassportInboundEventClassifier.IsInbound(Kind) && HasPaymentId;
}

/// <summary>
/// XPAY-602 — forma de una entrega de webhook de Passport.
/// </summary>
public enum PassportWebhookDeliveryShape
{
    /// <summary>No es JSON, la raíz no es objeto ni array, o algún elemento del array no es objeto.</summary>
    Invalid,

    /// <summary>Raíz objeto: un único evento — comportamiento original.</summary>
    SingleEvent,

    /// <summary>Raíz array con uno o más objetos.</summary>
    Batch,

    /// <summary>Raíz array sin elementos.</summary>
    EmptyBatch,
}

public static class PassportWebhookPayloadParser
{
    /// <summary>
    /// Parsea el cuerpo crudo a la proyección sanitizada. Devuelve
    /// <c>false</c> si el cuerpo no es un objeto JSON válido.
    /// </summary>
    public static bool TryParse(string rawBody, out PassportWebhookPayload payload)
    {
        payload = default!;
        var shape = TryParseDelivery(rawBody, out var payloads);
        if (shape != PassportWebhookDeliveryShape.SingleEvent) return false;
        payload = payloads[0];
        return true;
    }

    /// <summary>
    /// XPAY-602 — determina la FORMA de la entrega y proyecta sus eventos.
    ///
    /// Passport Sandbox entrega, en la práctica, LOTES con raíz JSON Array —
    /// contrario a los ejemplos de objeto único de su documentación. Se
    /// verificó con tráfico real en XPAY-601: 36 entregas con firma válida
    /// cuyos cuerpos el canonicalizador parseó sin problema, pero que el parser
    /// rechazó por exigir una raíz de tipo objeto.
    ///
    /// POLÍTICA FAIL-CLOSED: la validación estructural de TODOS los elementos
    /// ocurre ANTES de proyectar cualquiera. Si un solo elemento no es un
    /// objeto, se devuelve <see cref="PassportWebhookDeliveryShape.Invalid"/>
    /// con la lista vacía — nunca un lote a medias.
    /// </summary>
    public static PassportWebhookDeliveryShape TryParseDelivery(
        string rawBody, out IReadOnlyList<PassportWebhookPayload> payloads)
    {
        payloads = Array.Empty<PassportWebhookPayload>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return PassportWebhookDeliveryShape.Invalid;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                payloads = new[] { ParseElement(root) };
                return PassportWebhookDeliveryShape.SingleEvent;
            }

            if (root.ValueKind != JsonValueKind.Array)
                return PassportWebhookDeliveryShape.Invalid;

            // Pre-validación estructural completa del lote.
            var elements = new List<JsonElement>();
            foreach (var el in root.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    return PassportWebhookDeliveryShape.Invalid;
                elements.Add(el);
            }

            if (elements.Count == 0)
                return PassportWebhookDeliveryShape.EmptyBatch;

            var parsed = new List<PassportWebhookPayload>(elements.Count);
            foreach (var el in elements) parsed.Add(ParseElement(el));
            payloads = parsed;
            return PassportWebhookDeliveryShape.Batch;
        }
    }

    /// <summary>
    /// Proyecta UN evento. Es el mismo pipeline lógico que ya se usaba para la
    /// raíz de objeto único: el soporte de lotes no lo altera, sólo lo invoca
    /// por elemento.
    /// </summary>
    private static PassportWebhookPayload ParseElement(JsonElement root)
    {
        {
            // El nombre de evento viene en el campo "Event" — con E MAYÚSCULA,
            // según los ejemplos oficiales. Se acepta también "event" por
            // tolerancia, sin depender de ello.
            var eventName = Str(root, "Event") ?? Str(root, "event");

            var destKeyType = (string?)null;
            var destKeyPresent = false;
            if (root.TryGetProperty("destination_key", out var dk) && dk.ValueKind == JsonValueKind.Object)
            {
                destKeyType = Str(dk, "key_type");
                // key_value NO se proyecta: una llave Bre-B puede ser un
                // teléfono, un correo o un documento. Sólo se registra su
                // presencia.
                destKeyPresent = dk.TryGetProperty("key_value", out var kv)
                                 && kv.ValueKind == JsonValueKind.String
                                 && !string.IsNullOrWhiteSpace(kv.GetString());
            }

            string? errCode = null, errDesc = null;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                errCode = Str(err, "error_code");
                errDesc = Str(err, "error_description");
            }

            string? amountValue = null, amountCurrency = null;
            if (root.TryGetProperty("amount", out var amt) && amt.ValueKind == JsonValueKind.Object)
            {
                amountValue    = Str(amt, "value");
                amountCurrency = Str(amt, "currency");
            }

            return new PassportWebhookPayload(
                EventName:              eventName,
                Kind:                   PassportInboundEventClassifier.Classify(eventName),
                PaymentId:              Str(root, "payment_id"),
                Status:                 Str(root, "status"),
                AccountId:              Str(root, "account_id"),
                CreatedAt:              Str(root, "created_at"),
                UpdatedAt:              Str(root, "updated_at"),
                SettledAt:              Str(root, "settled_at"),
                DestinationKeyType:     destKeyType,
                DestinationKeyPresent:  destKeyPresent,
                QrCodeReference:        Str(root, "qr_code_reference"),
                EndToEndIdentification: Str(root, "end_to_end_identification"),
                ErrorCode:              errCode,
                ErrorDescription:       errDesc,
                AmountValue:            amountValue,
                AmountCurrency:         amountCurrency);
        }
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// SHA-256 hexadecimal del cuerpo crudo. Evidencia secundaria — nunca
    /// identidad primaria del evento — y nunca sustituye al cuerpo, que no se
    /// persiste.
    /// </summary>
    public static string ComputePayloadHash(string rawBody) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

    /// <summary>
    /// JSON sanitizado apto para persistir: sólo los campos proyectados, sin
    /// <c>sender</c>, sin <c>receiver</c> y sin el valor de la llave destino.
    /// </summary>
    public static string BuildSanitizedJson(PassportWebhookPayload p)
    {
        var o = new JsonObject
        {
            ["event"]                     = p.EventName,
            ["kind"]                      = p.Kind.ToString(),
            ["payment_id"]                = p.PaymentId,
            ["status"]                    = p.Status,
            ["account_id"]                = p.AccountId,
            ["created_at"]                = p.CreatedAt,
            ["updated_at"]                = p.UpdatedAt,
            ["settled_at"]                = p.SettledAt,
            ["destination_key_type"]      = p.DestinationKeyType,
            ["destination_key_present"]   = p.DestinationKeyPresent,
            ["qr_code_reference"]         = p.QrCodeReference,
            ["end_to_end_identification"] = p.EndToEndIdentification,
            ["error_code"]                = p.ErrorCode,
            ["error_description"]         = p.ErrorDescription,
            ["amount_value"]              = p.AmountValue,
            ["amount_currency"]           = p.AmountCurrency,
            // Marcador de auditoría. Deliberadamente NO nombra los campos
            // omitidos en inglés: el propio texto viajaría dentro del JSON
            // persistido y haría fallar los detectores de fuga de PII.
            ["campos_omitidos_por_privacidad"] = "remitente, destinatario, valor_de_llave",
        };
        return o.ToJsonString();
    }
}
