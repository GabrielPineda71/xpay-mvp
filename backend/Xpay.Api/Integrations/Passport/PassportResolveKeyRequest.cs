using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-324 — body de POST /v1/resolve-key (contrato confirmado vía
// docs.passportfintech.com/EN/resolve-key, XPAY-310, y consistente con la
// estructura real observada en Sandbox durante el diagnóstico previo).
//
// `Key` reutiliza PassportKeyRequest (ya existente, definido en
// PassportCreateKeyRequest.cs) — mismo shape exacto (key_type/key_value)
// confirmado por coincidencia de campos, no por semejanza de nombre.
//
// NOTA DE DIVERGENCIA (no oculta, no resuelta silenciosamente):
// docs.passportfintech.com documenta key_type como OPCIONAL para Resolve
// Key ("ENUM - PHONE, EMAIL, ID, ALPHA, BCODE (optional)"), a diferencia de
// Create Key donde es obligatorio. Este incremento, siguiendo el alcance
// explícitamente autorizado (que exige validar "key_type definido"),
// reutiliza PassportKeyRequest tal cual (KeyType no-nullable, validado como
// requerido en PassportKeyClient.ResolveKeyAsync) en vez de introducir un
// segundo DTO "key" con KeyType nullable sólo para este caso. Si en el
// futuro se confirma un escenario real de Resolve Key sin key_type, esto
// deberá revisarse — no se resuelve por inferencia aquí.
//
// customer_id: confirmado como UUID por la documentación oficial, mandatory.
//
// Nunca se loguea una instancia de este record (contiene key_value, que
// puede ser PII según el key_type, y un customer_id).
public sealed record PassportResolveKeyRequest(
    [property: JsonPropertyName("customer_id")] string CustomerId,
    [property: JsonPropertyName("key")]         PassportKeyRequest Key);
