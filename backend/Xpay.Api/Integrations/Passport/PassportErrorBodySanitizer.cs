using System.Text.Json;

namespace Xpay.Api.Integrations.Passport;

// XPAY-358 — extracción SEGURA, ALLOWLIST-based, de diagnóstico de error a
// partir del body de una respuesta HTTP no exitosa de Passport (cualquier
// status distinto de 401/403, que ya tienen su propio manejo dedicado en
// PassportHttpClient). Cierra el gap diagnóstico identificado en XPAY-355:
// antes de este ticket, el body de un error HTTP se descartaba por completo
// sin siquiera leerlo.
//
// DISEÑO FAIL-CLOSED — ante cualquier duda se descarta la información, NUNCA
// se arriesga a exponer algo potencialmente sensible:
//   - body ausente/vacío                    -> sin diagnóstico.
//   - body más grande que el límite          -> NUNCA se lee/parsea; sin diagnóstico.
//   - JSON inválido                          -> sin diagnóstico, sin excepción secundaria.
//   - raíz no es un objeto JSON              -> sin diagnóstico.
//   - campo no está en la allowlist          -> ignorado (incluyendo objetos/arrays anidados).
//   - campo en la allowlist pero no es texto -> ignorado (nunca se serializa un objeto/array).
//   - valor extraído contiene un patrón      -> el valor se DESCARTA POR COMPLETO (nunca se
//     sensible conocido                         "limpia"/recorta un valor sospechoso).
//   - valor extraído excede la longitud      -> se trunca ANTES de la comprobación de patrones
//     máxima permitida                          (nunca se expone un fragmento sin comprobar).
//
// Esta clase NUNCA conserva ni expone el body original — sólo produce, como
// máximo, dos cadenas cortas ya validadas (SafeErrorCode/SafeErrorMessage).
public static class PassportErrorBodySanitizer
{
    // Límite de tamaño del body considerado para diagnóstico (bytes UTF-8).
    // Un body más grande que esto NUNCA se parsea — se trata como
    // "diagnóstico no disponible", sin excepción, sin lectura parcial
    // expuesta (XPAY-358 §15).
    public const int MaxBodyLengthForDiagnosticsBytes = 4096;

    // Longitud máxima de cada valor extraído, ya truncado ANTES de la
    // comprobación de patrones sensibles — ningún valor expuesto puede
    // exceder este tamaño, incluso si provino de un campo "seguro" por
    // nombre.
    public const int MaxExtractedFieldLength = 200;

    // Únicos nombres de campo de nivel RAÍZ considerados candidatos. Se
    // prueban en el orden declarado; el primero que produzca un valor
    // válido y seguro gana. Cualquier otro campo del JSON (incluyendo
    // objetos anidados con estos mismos nombres) se ignora por completo —
    // nunca se recorre recursivamente el documento.
    private static readonly string[] CodeFieldNames    = { "code", "error_code" };
    private static readonly string[] MessageFieldNames = { "message", "error", "detail", "description" };

    // Subcadenas (case-insensitive) que, si aparecen en un valor candidato,
    // descartan ese valor POR COMPLETO. Deliberadamente amplio y con falsos
    // positivos aceptados (p. ej. rechazar un mensaje genuino que sólo
    // mencione la palabra "email" sin datos reales) — el diseño es
    // fail-closed, no "inteligente".
    private static readonly string[] SensitivePatterns =
    {
        "authorization", "bearer", "access_token", "client_secret",
        "api_key", "api_secret", "key_id", "key_value", "customer_id",
        "account_id", "identification_number", "qr_code_data",
        "qr_code_image", "email", "phone", "@",
    };

    // Secuencias de 7+ dígitos consecutivos — heurística adicional contra
    // números de cuenta/identificación/teléfono/BCODE embebidos en texto
    // libre, incluso sin una etiqueta reconocible cerca.
    private static readonly System.Text.RegularExpressions.Regex LongDigitRun =
        new(@"\d{7,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public sealed record SanitizedErrorInfo(string? SafeErrorCode, string? SafeErrorMessage)
    {
        public static readonly SanitizedErrorInfo Empty = new(null, null);
    }

    public static SanitizedErrorInfo Extract(string? rawBody)
    {
        if (string.IsNullOrEmpty(rawBody))
            return SanitizedErrorInfo.Empty;

        if (System.Text.Encoding.UTF8.GetByteCount(rawBody) > MaxBodyLengthForDiagnosticsBytes)
            return SanitizedErrorInfo.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return SanitizedErrorInfo.Empty;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return SanitizedErrorInfo.Empty;

            var code    = ExtractSafeString(doc.RootElement, CodeFieldNames);
            var message = ExtractSafeString(doc.RootElement, MessageFieldNames);
            return new SanitizedErrorInfo(code, message);
        }
    }

    private static string? ExtractSafeString(JsonElement root, string[] candidateFieldNames)
    {
        foreach (var fieldName in candidateFieldNames)
        {
            if (!root.TryGetProperty(fieldName, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.String)
                continue; // nunca objetos/arrays/números — sólo texto plano.

            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Length > MaxExtractedFieldLength)
                text = text[..MaxExtractedFieldLength];

            if (IsSensitive(text))
                continue; // descartado POR COMPLETO — nunca se "limpia" el valor.

            return text;
        }

        return null;
    }

    private static bool IsSensitive(string text)
    {
        if (LongDigitRun.IsMatch(text))
            return true;

        foreach (var pattern in SensitivePatterns)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
