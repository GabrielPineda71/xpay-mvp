using Microsoft.Extensions.Configuration;

namespace Xpay.PassportSandboxHarness;

// XPAY-325/326 — presencia (nunca valores) de las variables de entorno
// privadas específicas de cada caso de certificación, más allá de las 3
// genéricas ya cubiertas por HarnessConfigStatus (PASSPORT_BASE_URL/
// API_KEY/API_SECRET).
//
// Para create-key (M3-T1):
//   PASSPORT_TEST_ACCOUNT_ID   — account_id de la cuenta Sandbox ya
//                                provista por Passport (XPAY-323).
//   PASSPORT_TEST_NEW_KEY_TYPE / PASSPORT_TEST_NEW_KEY_VALUE — datos de una
//                                llave NUEVA, DESECHABLE, específica para el
//                                ciclo de vida de certificación. Deliberadamente
//                                DISTINTAS de PASSPORT_TEST_BREB_KEY/
//                                PASSPORT_TEST_BREB_KEY_TYPE (la BCODE que
//                                Passport ya entregó y que M3-T2 ya resolvió
//                                con éxito) — esa BCODE NO debe mutarse/
//                                eliminarse por certificación.
//
// Para suspend-key (M3-T3) y activate-key (M3-T4) — y su futura
// reutilización en delete-key (M3-T5/T7):
//   PASSPORT_TEST_NEW_KEY_ID  — el key_id REMOTO real que Passport asignó
//                               al crear la llave de certificación en
//                               M3-T1. XPAY-326 confirmó que este valor NO
//                               quedó persistido en ninguna parte tras esa
//                               ejecución (el harness de M3-T1 sólo computó
//                               su fingerprint en evidence.json y descartó
//                               el valor real al terminar el proceso) — por
//                               tanto esta variable, en el momento de
//                               XPAY-326, estaba ausente; XPAY-329 la
//                               recuperó realmente vía List Keys y la
//                               persistió privadamente. NUNCA se deriva de
//                               PASSPORT_TEST_NEW_KEY_VALUE (son conceptos
//                               distintos: uno es el dato que XPAY envió,
//                               el otro es el ID que Passport devolvió) ni
//                               de ningún fingerprint (SHA-256 no es
//                               reversible).
//
// Este código NUNCA abre ni parsea ~/.passport-sandbox.env directamente —
// lee exclusivamente desde IConfiguration (variables de entorno del
// proceso), mismo criterio que HarnessConfigStatus (XPAY-312).
public sealed record HarnessTargetConfig(
    bool AccountIdPresent,
    bool NewKeyTypePresent,
    bool NewKeyValuePresent,
    bool NewKeyIdPresent)
{
    public const string EnvAccountId   = "PASSPORT_TEST_ACCOUNT_ID";
    public const string EnvNewKeyType  = "PASSPORT_TEST_NEW_KEY_TYPE";
    public const string EnvNewKeyValue = "PASSPORT_TEST_NEW_KEY_VALUE";
    public const string EnvNewKeyId    = "PASSPORT_TEST_NEW_KEY_ID";

    public bool AllPresentForCreateKey  => AccountIdPresent && NewKeyTypePresent && NewKeyValuePresent;

    // XPAY-326/332 — compartido por suspend-key y activate-key (y por
    // delete-key en el futuro): las tres operan sobre una llave EXISTENTE
    // identificada únicamente por su key_id remoto — ninguna necesita
    // account_id/key_type/key_value.
    public bool AllPresentForExistingKeyOperations => NewKeyIdPresent;

    public static HarnessTargetConfig FromConfiguration(IConfiguration configuration) => new(
        AccountIdPresent:   !string.IsNullOrWhiteSpace(configuration[EnvAccountId]),
        NewKeyTypePresent:  !string.IsNullOrWhiteSpace(configuration[EnvNewKeyType]),
        NewKeyValuePresent: !string.IsNullOrWhiteSpace(configuration[EnvNewKeyValue]),
        NewKeyIdPresent:    !string.IsNullOrWhiteSpace(configuration[EnvNewKeyId]));

    // Único formato de impresión permitido — jamás el valor.
    public IEnumerable<string> ToRedactedLines()
    {
        yield return $"{EnvAccountId}={(AccountIdPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvNewKeyType}={(NewKeyTypePresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvNewKeyValue}={(NewKeyValuePresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvNewKeyId}={(NewKeyIdPresent ? "AVAILABLE" : "MISSING")}";
    }
}
