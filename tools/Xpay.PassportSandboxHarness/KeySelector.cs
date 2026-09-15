using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-328 — lógica PURA de selección determinística sobre una respuesta de
// List Keys (GET /v1/keys, XPAY-327/328), pensada para que una fase futura
// de recuperación de key_id la reutilice sin reimplementar el criterio de
// match. No hace I/O, no llama a Passport, no imprime nada — 100% testeable
// offline con datos sintéticos.
public enum KeySelectionOutcome
{
    NoMatch,
    ExactlyOneMatch,
    Ambiguous,
}

public sealed record KeySelectionResult(KeySelectionOutcome Outcome, PassportKeyResponse? SelectedKey);

public static class KeySelector
{
    // Criterio de match: account_id + key_type + key_value EXACTOS,
    // simultáneamente — nunca sólo uno o dos de los tres (p. ej. "el mismo
    // BCODE" no basta si el account_id difiere; "la misma cuenta" no basta
    // si el key_type/key_value difieren). Comparación ordinal (case-
    // sensitive): los account_id de Passport son UUID y los key_type se
    // comparan contra el literal exacto del enum (mismo criterio de
    // serialización ya usado en Create/Resolve Key — "PHONE", nunca
    // "MOBILE").
    //
    // 0 coincidencias  -> NoMatch (SelectedKey=null).
    // 1 coincidencia   -> ExactlyOneMatch (SelectedKey = esa llave).
    // >1 coincidencias -> Ambiguous (SelectedKey=null) — nunca se elige "la
    //                     primera" ni se desambigua por timestamp aquí; eso
    //                     queda diferido a un control secundario posterior
    //                     si alguna vez hace falta.
    public static KeySelectionResult SelectExactMatch(
        PassportListKeysResponse response,
        string expectedAccountId,
        PassportKeyType expectedKeyType,
        string expectedKeyValue)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKeyValue);

        var expectedKeyTypeLiteral = expectedKeyType.ToString();

        var matches = response.Keys
            .Where(candidate =>
                string.Equals(candidate.AccountId, expectedAccountId, StringComparison.Ordinal) &&
                string.Equals(candidate.Key?.KeyType, expectedKeyTypeLiteral, StringComparison.Ordinal) &&
                string.Equals(candidate.Key?.KeyValue, expectedKeyValue, StringComparison.Ordinal))
            .ToList();

        return matches.Count switch
        {
            0 => new KeySelectionResult(KeySelectionOutcome.NoMatch, null),
            1 => new KeySelectionResult(KeySelectionOutcome.ExactlyOneMatch, matches[0]),
            _ => new KeySelectionResult(KeySelectionOutcome.Ambiguous, null),
        };
    }
}
