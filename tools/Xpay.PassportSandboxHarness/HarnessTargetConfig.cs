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
// XPAY-340 — para resolve-key (M3-T2): recursos Bre-B de prueba YA
// provistos por Passport, NUNCA la llave nueva de certificación
// (PASSPORT_TEST_NEW_KEY_ID/_VALUE/_TYPE, target de Create/Suspend/
// Activate/Delete). Resolve identifica por customer_id+key_type+key_value
// — no por remote key_id.
public sealed record HarnessTargetConfig(
    bool AccountIdPresent,
    bool NewKeyTypePresent,
    bool NewKeyValuePresent,
    bool NewKeyIdPresent,
    bool CustomerIdPresent,
    bool BrebKeyTypePresent,
    bool BrebKeyValuePresent,
    bool QrKeyIdPresent,
    bool QrDecodeDataFilePathPresent,
    bool QrSuspendedKeyIdPresent,
    bool QrDeletedKeyIdPresent)
{
    public const string EnvAccountId   = "PASSPORT_TEST_ACCOUNT_ID";
    public const string EnvNewKeyType  = "PASSPORT_TEST_NEW_KEY_TYPE";
    public const string EnvNewKeyValue = "PASSPORT_TEST_NEW_KEY_VALUE";
    public const string EnvNewKeyId    = "PASSPORT_TEST_NEW_KEY_ID";
    // XPAY-340 — nombres YA existentes en ~/.passport-sandbox.env (usados
    // desde XPAY-317/322/324 para Resolve Key); este archivo nunca abre ni
    // parsea ese archivo directamente, sólo lee vía IConfiguration.
    public const string EnvCustomerId    = "PASSPORT_TEST_CUSTOMER_ID";
    public const string EnvBrebKeyType   = "PASSPORT_TEST_BREB_KEY_TYPE";
    public const string EnvBrebKeyValue  = "PASSPORT_TEST_BREB_KEY";

    // XPAY-460 — target DEDICADO de create-qr-static (M4-T1), deliberadamente
    // DISTINTO de PASSPORT_TEST_NEW_KEY_ID: Passport confirmó (Gustavo) que
    // "todos los QRs, por detrás, requieren una llave Bre-B activa", y la
    // llave identificada por PASSPORT_TEST_NEW_KEY_ID está DELETED (M3-T5/
    // M3-T7) — reutilizarla para M4 sería usar una llave muerta. El director
    // verificó visualmente en Passport Sandbox Dashboard (Products → Keys)
    // que existe una Key ACTIVE de tipo Business Entity Code asociada a una
    // cuenta de XPAY — su key_id real vive EXCLUSIVAMENTE en
    // ~/.passport-sandbox.env bajo este nombre nuevo, nunca hardcodeado,
    // nunca derivado de PASSPORT_TEST_NEW_KEY_ID ni de ningún valor
    // histórico. NO existe fallback: si esta variable falta, create-qr-static
    // se bloquea LOCALMENTE (AllPresentForCreateQrStatic abajo), incluso si
    // PASSPORT_TEST_NEW_KEY_ID SÍ está presente — ver
    // AllPresentForCreateQrStatic.
    public const string EnvQrKeyId = "PASSPORT_TEST_QR_KEY_ID";

    // XPAY-465 — target de decode-qr-static (M4-T2). Passport exige, para
    // POST /v1/qrcodes/decode, exactamente customer_id + qr_code_data (ver
    // PassportDecodeQrCodeRequest.cs/PassportQrClient.ValidateDecodeRequest
    // — NUNCA id ni qr_code_reference). customer_id reutiliza
    // PASSPORT_TEST_CUSTOMER_ID (mismo recurso que create-qr-static/
    // resolve-key). qr_code_data es POTENCIALMENTE SENSIBLE (payload EMVCo
    // de un QR de pago real) — por eso esta variable NO contiene el dato en
    // sí, sino la RUTA a un archivo local privado que lo contiene (fuera de
    // Git, permisos restrictivos — responsabilidad del operador al
    // crearlo). Esta clase, igual que con el resto de targets, sólo
    // confirma la PRESENCIA de la variable (la ruta) — nunca abre ni lee el
    // archivo, y mucho menos su contenido; eso es responsabilidad exclusiva
    // de DecodeQrStaticExecutor en el momento de --execute, nunca en
    // dry-run/Prepare.
    public const string EnvQrDecodeDataFilePath = "PASSPORT_TEST_QR_DECODE_DATA_FILE";

    // XPAY-471 — target DEDICADO de create-qr-static-suspended-key (M4-T3-A).
    // Apunta a una llave DESECHABLE dedicada, todavía NO creada ni
    // suspendida (eso queda fuera de alcance de XPAY-471, que es soporte de
    // código únicamente) — su key_id real vivirá, cuando exista,
    // EXCLUSIVAMENTE en ~/.passport-sandbox.env bajo este nombre, nunca
    // hardcodeado. PROHIBIDO cualquier fallback hacia PASSPORT_TEST_QR_KEY_ID
    // (llave ACTIVA protegida de M4-T1/M4-T2) o PASSPORT_TEST_NEW_KEY_ID
    // (llave DELETED de M3, target exclusivo de M4-T3-B) — ver
    // AllPresentForCreateQrStaticSuspendedKey.
    public const string EnvQrSuspendedKeyId = "PASSPORT_TEST_QR_SUSPENDED_KEY_ID";

    // XPAY-492 — target DEDICADO de create-qr-static-deleted-key (M4-T3-B),
    // reemplaza a PASSPORT_TEST_NEW_KEY_ID como fuente de este subcaso.
    // Motivo: XPAY-487/488/490/491 confirmaron que tanto
    // PASSPORT_TEST_NEW_KEY_ID como PASSPORT_TEST_NEW_KEY_VALUE quedaron
    // contaminados (ambos coinciden hoy con la llave ACTIVA de M4-T1/T2) y
    // que el key_id/key_value histórico de M3 no es recuperable localmente
    // — ni siquiera vía List Keys (XPAY-490 lo confirmó con una llamada
    // real). Dirección decidió abandonar la reutilización de esa llave
    // histórica: M4-T3-B usa en adelante una llave DESECHABLE dedicada,
    // creada y eliminada específicamente para este subcaso (ver
    // create-m4-t3-deleted-fixture-key/delete-m4-t3-fixture-key abajo). Su
    // key_id real vivirá, cuando exista, EXCLUSIVAMENTE en
    // ~/.passport-sandbox.env bajo este nombre nuevo — nunca hardcodeado,
    // nunca derivado de PASSPORT_TEST_NEW_KEY_ID/_VALUE (los valores
    // contaminados). PROHIBIDO cualquier fallback hacia
    // PASSPORT_TEST_NEW_KEY_ID (llave M3 contaminada/abandonada),
    // PASSPORT_TEST_QR_KEY_ID (llave ACTIVA protegida) o
    // PASSPORT_TEST_QR_SUSPENDED_KEY_ID (target exclusivo de M4-T3-A).
    public const string EnvQrDeletedKeyId = "PASSPORT_TEST_QR_DELETED_KEY_ID";

    public bool AllPresentForCreateKey  => AccountIdPresent && NewKeyTypePresent && NewKeyValuePresent;

    // XPAY-326/332/334/336 — compartido por suspend-key/activate-key/
    // delete-key/delete-already-deleted-key: las cuatro operan sobre una
    // llave EXISTENTE identificada únicamente por su key_id remoto —
    // ninguna necesita account_id/key_type/key_value ni los recursos Bre-B
    // de prueba de Resolve.
    public bool AllPresentForExistingKeyOperations => NewKeyIdPresent;

    // XPAY-340 — target de resolve-key: customer_id + key_type/key_value
    // del recurso Bre-B YA provisto por Passport (nunca la llave nueva de
    // certificación).
    public bool AllPresentForResolveKey => CustomerIdPresent && BrebKeyTypePresent && BrebKeyValuePresent;

    // XPAY-344 — target compartido por create-key-missing y
    // create-key-invalid (M3-T6): ambos reutilizan account_id + key_type
    // YA existentes de Create Key (PASSPORT_TEST_ACCOUNT_ID/
    // PASSPORT_TEST_NEW_KEY_TYPE) — deliberadamente SIN exigir
    // PASSPORT_TEST_NEW_KEY_VALUE, porque ninguno de los dos comandos lee
    // esa variable: MISSING omite key_value a propósito (es el campo bajo
    // prueba); INVALID genera su propio key_value sintéticamente inválido
    // dentro del executor, nunca desde el env.
    public bool AllPresentForAccountAndKeyType => AccountIdPresent && NewKeyTypePresent;

    // XPAY-351/XPAY-460 — target de create-qr-static (M4-T1):
    // PASSPORT_TEST_QR_KEY_ID (NUEVO en XPAY-460 — la Key ACTIVE de tipo
    // Business Entity Code verificada por el director en Passport Sandbox
    // Dashboard, DISTINTA de la llave DELETED de M3) como key_id del QR, y
    // PASSPORT_TEST_CUSTOMER_ID (mismo recurso ya usado por resolve-key)
    // como customer_id del QR. Los demás campos del contrato (type/channel/
    // vat/inc/tip/qr_code_reference — ver XPAY-458/460; additional_info ya
    // no se envía para M4-T1) son constantes o valores generados de
    // protocolo NO sensibles, fijados en CreateQrStaticExecutor — no son
    // "targets" porque no identifican un recurso privado de Sandbox.
    //
    // XPAY-460 — DELIBERADAMENTE NewKeyIdPresent NO forma parte de esta
    // condición: PASSPORT_TEST_NEW_KEY_ID (la llave DELETED de M3) nunca
    // debe servir de fallback, ni siquiera si está presente y
    // PASSPORT_TEST_QR_KEY_ID falta — en ese caso el resultado debe seguir
    // siendo AbortedTargetMissing (ver HarnessOrchestrator, y el test
    // dedicado que prueba exactamente este escenario).
    public bool AllPresentForCreateQrStatic => QrKeyIdPresent && CustomerIdPresent;

    // XPAY-506 — target de create-qr-dynamic (M4-T4): MISMOS dos targets
    // exactos que create-qr-static (M4-T1) — PASSPORT_TEST_QR_KEY_ID +
    // PASSPORT_TEST_CUSTOMER_ID. Propiedad dedicada (no se reutiliza
    // AllPresentForCreateQrStatic) siguiendo el mismo criterio ya aplicado
    // a cada comando del harness — un target propio y nombrado por
    // comando, aunque hoy sea idéntico a otro, para poder evolucionar cada
    // uno de forma independiente sin acoplar dos comandos por accidente.
    // Sin fallback hacia PASSPORT_TEST_NEW_KEY_ID/_VALUE (contaminadas),
    // PASSPORT_TEST_QR_SUSPENDED_KEY_ID (M4-T3-A) ni
    // PASSPORT_TEST_QR_DELETED_KEY_ID (M4-T3-B).
    public bool AllPresentForCreateQrDynamic => QrKeyIdPresent && CustomerIdPresent;

    // XPAY-517 — target de decode-qr-dynamic (M4-T5): MISMOS dos targets
    // que create-qr-dynamic (PASSPORT_TEST_QR_KEY_ID + PASSPORT_TEST_CUSTOMER_ID)
    // — aquí QrKeyId se usa ÚNICAMENTE para el chequeo de identidad contra
    // el artefacto privado de M4-T4 (nunca se envía en el request de
    // Decode, que sólo lleva customer_id + qr_code_data). Los destinos
    // privados (lectura del artefacto M4-T4, escritura del artefacto
    // propio de M4-T5) se validan exclusivamente dentro de
    // DecodeQrDynamicExecutor.ExecuteAsync — mismo criterio ya establecido
    // para PASSPORT_TEST_M4_T4_PRIVATE_CAPTURE_DIR (XPAY-508): nunca
    // forman parte de este chequeo de presencia genérico de dry-run.
    public bool AllPresentForDecodeQrDynamic => QrKeyIdPresent && CustomerIdPresent;

    // XPAY-465 — target de decode-qr-static (M4-T2): la RUTA al archivo
    // local privado con el qr_code_data real, + customer_id. Sin fallback a
    // ningún otro dato (nunca deriva qr_code_data de PASSPORT_TEST_NEW_KEY_ID/
    // PASSPORT_TEST_QR_KEY_ID/qr_code_reference — son conceptos distintos).
    public bool AllPresentForDecodeQrStatic => QrDecodeDataFilePathPresent && CustomerIdPresent;

    // XPAY-471 — target de create-qr-static-suspended-key (M4-T3-A):
    // PASSPORT_TEST_QR_SUSPENDED_KEY_ID + PASSPORT_TEST_CUSTOMER_ID.
    // DELIBERADAMENTE ni QrKeyIdPresent ni NewKeyIdPresent forman parte de
    // esta condición — sin fallback hacia la llave activa protegida ni
    // hacia la llave deleted de M3-T3-B.
    public bool AllPresentForCreateQrStaticSuspendedKey => QrSuspendedKeyIdPresent && CustomerIdPresent;

    // XPAY-492 (reemplaza el criterio original de XPAY-471) — target de
    // create-qr-static-deleted-key (M4-T3-B): PASSPORT_TEST_QR_DELETED_KEY_ID
    // (llave desechable dedicada de M4-T3-B, nunca la histórica de M3,
    // contaminada/abandonada) + PASSPORT_TEST_CUSTOMER_ID (customer_id
    // VÁLIDO — lo único deliberadamente incorrecto en M4-T3 es el target de
    // M4-T3-C, no éste). DELIBERADAMENTE NewKeyIdPresent YA NO forma parte
    // de esta condición — sin fallback hacia la llave M3 contaminada.
    public bool AllPresentForCreateQrStaticDeletedKey => QrDeletedKeyIdPresent && CustomerIdPresent;

    // XPAY-471 — target de create-qr-static-invalid-customer (M4-T3-C):
    // ÚNICAMENTE PASSPORT_TEST_QR_KEY_ID (llave ACTIVA protegida, usada
    // sólo de forma read/reference). CustomerIdPresent NO forma parte de
    // esta condición — el customer_id nunca se lee del entorno para este
    // subcaso, siempre proviene de InvalidCustomerIdGenerator.Generate().
    public bool AllPresentForCreateQrStaticInvalidCustomer => QrKeyIdPresent;

    // XPAY-474 — target de create-m4-t3-suspended-fixture-key (preparación
    // de M4-T3-A): ÚNICAMENTE PASSPORT_TEST_ACCOUNT_ID (recurso COMPARTIDO
    // y estable, la cuenta Sandbox misma — mismo criterio ya usado con
    // PASSPORT_TEST_CUSTOMER_ID reutilizado de forma segura en varios
    // comandos). Deliberadamente NO incluye NewKeyTypePresent/
    // NewKeyValuePresent (key_type es constante BCODE, key_value se genera
    // internamente — DisposableBcodeGenerator — nunca desde el entorno) ni
    // QrKeyIdPresent/QrSuspendedKeyIdPresent (esta operación CREA una
    // llave, no depende de ninguna llave existente).
    public bool AllPresentForCreateM4T3SuspendedFixtureKey => AccountIdPresent;

    // XPAY-474 — target de suspend-m4-t3-fixture-key (preparación de
    // M4-T3-A): ÚNICAMENTE PASSPORT_TEST_QR_SUSPENDED_KEY_ID — la misma
    // variable que create-m4-t3-suspended-fixture-key deja pendiente de
    // configuración manual tras su ejecución real, y que
    // create-qr-static-suspended-key (M4-T3-A) ya consume. DELIBERADAMENTE
    // ni NewKeyIdPresent ni QrKeyIdPresent forman parte de esta condición —
    // sin fallback hacia la llave DELETED de M3 ni hacia la llave ACTIVA
    // protegida de M4-T1/T2.
    public bool AllPresentForSuspendM4T3FixtureKey => QrSuspendedKeyIdPresent;

    // XPAY-489 — target de recover-deleted-key-id (recuperación read-only,
    // vía List Keys, del key_id histórico DELETED del ciclo M3):
    // PASSPORT_TEST_ACCOUNT_ID + PASSPORT_TEST_NEW_KEY_TYPE +
    // PASSPORT_TEST_NEW_KEY_VALUE — los tres criterios de búsqueda exactos
    // de KeySelector.SelectExactMatch. DELIBERADAMENTE no incluye
    // NewKeyIdPresent: PASSPORT_TEST_NEW_KEY_ID es precisamente el valor
    // hoy no confiable (XPAY-487/488) que este comando existe para
    // validar/reemplazar, nunca una entrada de su propia búsqueda.
    public bool AllPresentForRecoverDeletedKeyId =>
        AccountIdPresent && NewKeyTypePresent && NewKeyValuePresent;

    // XPAY-492 — target de create-m4-t3-deleted-fixture-key (preparación,
    // paso 1, de M4-T3-B): ÚNICAMENTE PASSPORT_TEST_ACCOUNT_ID — mismo
    // criterio exacto que create-m4-t3-suspended-fixture-key (XPAY-474):
    // key_type es constante BCODE, key_value se genera internamente
    // (DisposableBcodeGenerator, reutilizado sin cambios), nunca desde el
    // entorno; esta operación CREA una llave, no depende de ninguna
    // existente.
    public bool AllPresentForCreateM4T3DeletedFixtureKey => AccountIdPresent;

    // XPAY-492 — target de delete-m4-t3-fixture-key (preparación, paso 2,
    // de M4-T3-B): ÚNICAMENTE PASSPORT_TEST_QR_DELETED_KEY_ID — la misma
    // variable que create-m4-t3-deleted-fixture-key deja pendiente de
    // configuración manual tras su ejecución real, y que
    // create-qr-static-deleted-key (M4-T3-B final) ya consume.
    // DELIBERADAMENTE ni NewKeyIdPresent ni QrKeyIdPresent ni
    // QrSuspendedKeyIdPresent forman parte de esta condición — sin
    // fallback hacia la llave M3 contaminada, la llave ACTIVA protegida, ni
    // la llave suspendida de M4-T3-A.
    public bool AllPresentForDeleteM4T3FixtureKey => QrDeletedKeyIdPresent;

    public static HarnessTargetConfig FromConfiguration(IConfiguration configuration) => new(
        AccountIdPresent:    !string.IsNullOrWhiteSpace(configuration[EnvAccountId]),
        NewKeyTypePresent:   !string.IsNullOrWhiteSpace(configuration[EnvNewKeyType]),
        NewKeyValuePresent:  !string.IsNullOrWhiteSpace(configuration[EnvNewKeyValue]),
        NewKeyIdPresent:     !string.IsNullOrWhiteSpace(configuration[EnvNewKeyId]),
        CustomerIdPresent:   !string.IsNullOrWhiteSpace(configuration[EnvCustomerId]),
        BrebKeyTypePresent:  !string.IsNullOrWhiteSpace(configuration[EnvBrebKeyType]),
        BrebKeyValuePresent: !string.IsNullOrWhiteSpace(configuration[EnvBrebKeyValue]),
        QrKeyIdPresent:      !string.IsNullOrWhiteSpace(configuration[EnvQrKeyId]),
        QrDecodeDataFilePathPresent: !string.IsNullOrWhiteSpace(configuration[EnvQrDecodeDataFilePath]),
        QrSuspendedKeyIdPresent:     !string.IsNullOrWhiteSpace(configuration[EnvQrSuspendedKeyId]),
        QrDeletedKeyIdPresent:       !string.IsNullOrWhiteSpace(configuration[EnvQrDeletedKeyId]));

    // Único formato de impresión permitido — jamás el valor (y, para
    // EnvQrDecodeDataFilePath, ni siquiera la ruta — sólo AVAILABLE/MISSING,
    // igual que el resto).
    public IEnumerable<string> ToRedactedLines()
    {
        yield return $"{EnvAccountId}={(AccountIdPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvNewKeyType}={(NewKeyTypePresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvNewKeyValue}={(NewKeyValuePresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvNewKeyId}={(NewKeyIdPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvCustomerId}={(CustomerIdPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvBrebKeyType}={(BrebKeyTypePresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvBrebKeyValue}={(BrebKeyValuePresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvQrKeyId}={(QrKeyIdPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvQrDecodeDataFilePath}={(QrDecodeDataFilePathPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvQrSuspendedKeyId}={(QrSuspendedKeyIdPresent ? "AVAILABLE" : "MISSING")}";
        yield return $"{EnvQrDeletedKeyId}={(QrDeletedKeyIdPresent ? "AVAILABLE" : "MISSING")}";
    }
}
