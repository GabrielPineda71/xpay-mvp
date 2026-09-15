using Microsoft.Extensions.Configuration;

namespace Xpay.PassportSandboxHarness;

// XPAY-325 — presencia (nunca valores) de las variables de entorno privadas
// específicas de cada caso de certificación, más allá de las 3 genéricas ya
// cubiertas por HarnessConfigStatus (PASSPORT_BASE_URL/API_KEY/API_SECRET).
//
// Para create-key (M3-T1):
//   PASSPORT_TEST_ACCOUNT_ID   — account_id de la cuenta Sandbox ya
//                                provista por Passport (XPAY-323).
//   PASSPORT_TEST_NEW_KEY_TYPE / PASSPORT_TEST_NEW_KEY_VALUE — datos de una
//                                llave NUEVA, DESECHABLE, específica para el
//                                ciclo de vida de certificación (Create→
//                                Suspend→Activate→Delete). Deliberadamente
//                                DISTINTAS de PASSPORT_TEST_BREB_KEY/
//                                PASSPORT_TEST_BREB_KEY_TYPE (la BCODE que
//                                Passport ya entregó y que M3-T2 ya resolvió
//                                con éxito) — esa BCODE NO debe mutarse/
//                                eliminarse por certificación (XPAY-325
//                                objetivo #6).
//
// Este código NUNCA abre ni parsea ~/.passport-sandbox.env directamente —
// lee exclusivamente desde IConfiguration (variables de entorno del
// proceso), mismo criterio que HarnessConfigStatus (XPAY-312).
public sealed record HarnessTargetConfig(
    bool AccountIdPresent,
    bool NewKeyTypePresent,
    bool NewKeyValuePresent)
{
    public const string EnvAccountId  = "PASSPORT_TEST_ACCOUNT_ID";
    public const string EnvNewKeyType  = "PASSPORT_TEST_NEW_KEY_TYPE";
    public const string EnvNewKeyValue = "PASSPORT_TEST_NEW_KEY_VALUE";

    public bool AllPresentForCreateKey => AccountIdPresent && NewKeyTypePresent && NewKeyValuePresent;

    public static HarnessTargetConfig FromConfiguration(IConfiguration configuration) => new(
        AccountIdPresent:  !string.IsNullOrWhiteSpace(configuration[EnvAccountId]),
        NewKeyTypePresent: !string.IsNullOrWhiteSpace(configuration[EnvNewKeyType]),
        NewKeyValuePresent: !string.IsNullOrWhiteSpace(configuration[EnvNewKeyValue]));

    // Único formato de impresión permitido — jamás el valor.
    public IEnumerable<string> ToRedactedLines()
    {
        yield return $"{EnvAccountId}={(AccountIdPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvNewKeyType}={(NewKeyTypePresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvNewKeyValue}={(NewKeyValuePresent ? "AVAILABLE" : "MISSING")}";
    }
}
