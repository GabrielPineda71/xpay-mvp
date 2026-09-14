using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — tipo de QR Bre-B para Create QR Code, contrato canónico
// confirmado en XPAY-297/298 vía docs.passportfintech.com/EN/create-qr-codes:
// STATIC, DYNAMIC. Es el mismo endpoint (POST /v1/qrcodes) para ambos — se
// diferencian sólo por este campo.
//
// [JsonConverter(JsonStringEnumConverter)] aplicado LOCALMENTE al tipo — mismo
// patrón ya verificado empíricamente en PassportKeyType (XPAY-287/289):
// serializa cada miembro exactamente como su nombre en mayúsculas, sin tocar
// JsonSerializerOptions global. Enum.IsDefined se usa en PassportQrClient
// para rechazar valores fuera de rango ANTES de serializar/HTTP (mismo
// hallazgo corregido en Create Key — JsonStringEnumConverter no rechaza por
// sí solo un valor de enum construido fuera de rango).
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PassportQrType
{
    STATIC,
    DYNAMIC,
}
