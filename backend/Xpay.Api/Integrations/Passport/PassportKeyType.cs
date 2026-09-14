using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-287 — enum de key_type para Create Bre-B Key, contrato canónico
// confirmado en XPAY-286 vía docs.passportfintech.com/ES/create-keys:
// ID, PHONE, EMAIL, ALPHA, BCODE.
//
// IMPORTANTE: "PHONE", NO "MOBILE". La página /EN/creating-breb-keys (más
// antigua/desactualizada) documentaba "MOBILE" — XPAY-286 reconcilió esa
// discrepancia contra la página específica y actual del endpoint, que usa
// "PHONE" — exactamente el mismo valor que Resolve Key y que el
// ValidKeyTypes/CHECK constraint interno de XPAY ya usan (BrebService.cs,
// migración 010). No se introduce "MOBILE" ni ningún mapping PHONE↔MOBILE:
// no hace falta.
//
// [JsonConverter(JsonStringEnumConverter)] aplicado LOCALMENTE al tipo —
// verificado empíricamente (XPAY-287) que serializa cada miembro exactamente
// como su nombre en mayúsculas ("ID", "PHONE", etc.) sin necesidad de tocar
// JsonSerializerOptions global ni tocar PassportHttpClient.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PassportKeyType
{
    ID,
    PHONE,
    EMAIL,
    ALPHA,
    BCODE,
}
