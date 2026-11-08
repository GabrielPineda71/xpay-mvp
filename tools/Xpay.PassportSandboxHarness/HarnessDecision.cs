namespace Xpay.PassportSandboxHarness;

// XPAY-312/325/326 — decisión de acción del harness a partir de los
// argumentos de línea de comandos ÚNICAMENTE (función pura, sin I/O, sin
// red — testeable offline). El comportamiento por defecto es SIEMPRE
// seguro: sin argumentos, comando desconocido, o sin AMBAS banderas de
// mutación (--execute + la bandera de confirmación específica del
// comando), nunca se llega a Execute.
public enum HarnessAction
{
    ShowHelp,
    Aborted,
    DryRun,
    Execute,
}

// XPAY-325/326/332/334/336 — comando reconocido por el harness. Se
// generaliza desde el único comando "create-customer" de XPAY-312 para
// soportar múltiples casos de certificación sin duplicar la lógica de
// parsing/guards.
public enum HarnessCommand
{
    Unknown,
    CreateCustomer,
    CreateKey,
    SuspendKey,
    ActivateKey,
    DeleteKey,
    DeleteAlreadyDeletedKey,
    ResolveKey,
    // XPAY-344 — M3-T6 (negative testing de Create Key): MISSING nunca
    // llega a Passport (bloqueo LOCAL garantizado por diseño — ver
    // CreateKeyMissingExecutor); INVALID sí realiza una llamada HTTP real
    // futura (no en XPAY-344), con un key_value sintéticamente inválido
    // generado internamente. DUPLICATE permanece BLOCKED_PENDING_CONTRACT_
    // CONFIRMATION — deliberadamente SIN comando ejecutable en el harness.
    CreateKeyMissing,
    CreateKeyInvalid,
    // XPAY-351 — M4-T1 (QR estático): construido sobre IPassportQrClient
    // (stack productivo distinto de IPassportKeyClient, pero mismo
    // IPassportHttpClient/IPassportTokenProvider subyacente — sin segundo
    // stack HTTP). Comando y bandera de confirmación EXCLUSIVOS de este
    // caso: ninguna confirmación de M3 lo autoriza, y su propia
    // confirmación no autoriza ningún comando de M3.
    CreateQrStatic,
    // XPAY-465 — M4-T2 (Decode QR estático): mismo IPassportQrClient que
    // CreateQrStatic, método DecodeQrCodeAsync (distinto de
    // CreateQrCodeAsync). Comando y bandera de confirmación EXCLUSIVOS —
    // --confirm-create-qr-static NUNCA autoriza decode-qr-static, y
    // viceversa (mismo criterio de no-conflación ya aplicado a cada par de
    // comandos anterior).
    DecodeQrStatic,
    // XPAY-471 — M4-T3 (casos de error de QR estático): tres subcasos
    // INDEPENDIENTES entre sí y de create-qr-static/decode-qr-static —
    // cada uno con su propio comando y bandera de confirmación exclusiva,
    // mismo criterio de no-conflación ya aplicado a cada par anterior.
    CreateQrStaticSuspendedKey,
    CreateQrStaticDeletedKey,
    CreateQrStaticInvalidCustomer,
    // XPAY-474 — preparación de la llave desechable de M4-T3-A: dos
    // operaciones INDEPENDIENTES entre sí y de create-key/suspend-key (M3)
    // y de create-qr-static-suspended-key (M4-T3-A) — cada una con su
    // propio comando y bandera de confirmación exclusiva.
    CreateM4T3SuspendedFixtureKey,
    SuspendM4T3FixtureKey,
    // XPAY-489 — recuperación read-only del key_id histórico DELETED del
    // ciclo M3, vía List Keys. Completamente independiente de las dos
    // anteriores (ninguna comparte confirmación ni target).
    RecoverDeletedKeyId,
    // XPAY-492 — preparación de la llave desechable de M4-T3-B (fixture
    // dedicado, reemplaza el intento de reutilizar la llave histórica
    // contaminada de M3): dos operaciones independientes entre sí y de
    // cualquier otro comando (ninguna comparte confirmación ni target).
    CreateM4T3DeletedFixtureKey,
    DeleteM4T3FixtureKey,
    // XPAY-506 — M4-T4 (Create QR Code DINÁMICO). Comando positivo
    // independiente de todos los anteriores.
    CreateQrDynamic,
    // XPAY-517 — M4-T5 (Decode QR Code DINÁMICO), reutiliza EXCLUSIVAMENTE
    // el QR ya creado en M4-T4 (nunca crea un QR nuevo). Comando
    // independiente de create-qr-dynamic y de decode-qr-static.
    DecodeQrDynamic,
    // XPAY-522 — M4-T6 (casos de error de QR DYNAMIC), tres subcasos
    // INDEPENDIENTES entre sí y de todos los anteriores — mismo criterio
    // de no-conflación ya aplicado a cada par de comandos anterior.
    CreateQrDynamicSuspendedKey,
    CreateQrDynamicDeletedKey,
    CreateQrDynamicInvalidCustomer,
    // XPAY-529 — M5 (Create/Retrieve Bre-B Payment, infraestructura de
    // certificación). Comandos independientes entre sí y de todos los
    // anteriores — mismo criterio de no-conflación. NINGUNA confirmación
    // M1-M4 los autoriza, y viceversa.
    CreateBrebPaymentCert,
    RetrieveBrebPaymentCert,
    // XPAY-531 — M5-T1 (Create/Link Customer B + Account B), infraestructura
    // de certificación. Comandos independientes entre sí y de todos los
    // anteriores — mismo criterio de no-conflación. NINGUNA confirmación
    // M1-M4/M5-Payment los autoriza, y viceversa. Account B debe ejecutarse
    // DESPUÉS de Customer B (obtiene customer_id exclusivamente de su
    // artefacto privado) pero cada uno conserva su propia bandera.
    CreateM5CustomerB,
    CreateM5AccountB,
    // XPAY-545 — M5-T2-RESOLVE: Resolve Key dedicado a la certificación de
    // Payment B->A, independiente de M3-T2 (congelado/ACCEPTED) y de todos
    // los anteriores — mismo criterio de no-conflación.
    ResolveKeyM5T2,
    // XPAY-546 — transferencia LOCAL (nunca llama a Passport) del
    // resolution_id real ya capturado privadamente hacia
    // PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID. Independiente de todos los
    // anteriores.
    ConfigureM5PaymentResolution,
    // XPAY-570 — M5-T5-MISSING-AMOUNT (negative testing de Create Payment):
    // nunca llega a Passport (bloqueo LOCAL garantizado por diseño — ver
    // CreatePaymentMissingAmountExecutor). Independiente de todos los
    // anteriores, incluido CreateBrebPaymentCert.
    CreatePaymentMissingAmount,
    // XPAY-571 — M5-T5-MISSING-ACCOUNT (negative testing de Create Payment,
    // campo invertido respecto a XPAY-570): nunca llega a Passport (bloqueo
    // LOCAL garantizado por diseño — ver CreatePaymentMissingAccountExecutor).
    // Independiente de todos los anteriores.
    CreatePaymentMissingAccount,
    // XPAY-572 — M5-T5-INVALID-RESOLUTION: A DIFERENCIA de MissingAmount/
    // MissingAccount, ESTE comando SÍ realiza una llamada HTTP real a
    // Passport (resolution_id sintético, nunca vacío — el guard productivo
    // de AccountId/Amount no dispara). Repeat guard PERMANENTE propio (ver
    // CreatePaymentInvalidResolutionExecutor) — nunca se ejecuta dos veces.
    // Independiente de todos los anteriores, incluido CreateBrebPaymentCert.
    CreatePaymentInvalidResolution,
    // XPAY-575 — FASE 1 (Create Key) de un fixture NUEVO y desechable para
    // M5-T5-D (suspended key). NUNCA suspende/activa/elimina/resuelve —
    // ÚNICAMENTE Create Key. Repeat guard PERMANENTE propio. Independiente
    // de create-key (M3) y de create-m4-t3-suspended-fixture-key (M4).
    CreateM5T5SuspendedKeyFixture,
    // XPAY-576 — FASE 2 (Suspend Key) del MISMO fixture creado en
    // XPAY-575, leído exclusivamente desde su private capture. NUNCA
    // Create/Resolve/Payment/Delete. Repeat guard PERMANENTE propio,
    // independiente del de XPAY-575. Independiente de suspend-key (M3) y
    // de suspend-m4-t3-fixture-key (M4).
    SuspendM5T5FixtureKey,
    // XPAY-577 — FASE 3 (Resolve real) y EJECUCIÓN FINAL de M5-T5-D.
    // Exige correlación Create->Suspend válida (XPAY-575->XPAY-576) antes
    // de HTTP. NUNCA Create/Suspend/Payment/Retrieve/Delete/Reactivate.
    // Repeat guard PERMANENTE propio.
    ResolveM5T5SuspendedKey,
    // XPAY-613 — FASE A (PREP-CREATE) de un fixture NUEVO y desechable para
    // M5-T5-E (deleted key). NUNCA elimina/suspende/activa/resuelve —
    // ÚNICAMENTE Create Key. Repeat guard PERMANENTE propio. Independiente
    // de create-key (M3), de create-m4-t3-deleted-fixture-key (M4) y de
    // create-m5-t5-suspended-fixture (M5-T5-D).
    CreateM5T5DeletedKeyFixture,
    // XPAY-613 — FASE B (PREP-DELETE) del MISMO fixture creado en la Fase A,
    // leído exclusivamente desde su private capture. Operación IRREVERSIBLE.
    // NUNCA Create/Suspend/Activate/Resolve/Payment. Repeat guard PERMANENTE
    // propio, independiente del de la Fase A. Independiente de delete-key
    // (M3-T5) y de delete-m4-t3-fixture-key (M4).
    DeleteM5T5FixtureKey,
    // XPAY-613 — FASE C (RESOLVE-DELETED) y EJECUCIÓN FINAL de M5-T5-E.
    // Exige correlación Create->Delete válida (Fase A -> Fase B) antes de
    // HTTP. NUNCA Create/Suspend/Delete/Payment/Retrieve/Reactivate. Repeat
    // guard PERMANENTE propio.
    ResolveM5T5DeletedKey,
    // XPAY-619 — M5-T5-C: único Payment con monto por encima del umbral
    // certificable del Anexo (>12.000.000 COP), esperando rechazo de
    // Passport. Repeat guard PERMANENTE propio, consumido ANTES del HTTP.
    // NUNCA Resolve/Retrieve/mutación de llaves. Independiente del Payment
    // positivo (M5-PAYMENT-CREATE) y de los otros negativos de pago.
    CreatePaymentOverLimit,
}

public static class HarnessDecision
{
    public const string CommandCreateCustomer         = "create-customer";
    public const string CommandCreateKey               = "create-key";
    public const string CommandSuspendKey              = "suspend-key";
    public const string CommandActivateKey             = "activate-key";
    public const string CommandDeleteKey               = "delete-key";
    // XPAY-336 — M3-T7: comando explícitamente INDEPENDIENTE de
    // delete-key (M3-T5), aunque ambos invoquen el mismo
    // IPassportKeyClient.DeleteKeyAsync — nunca se confunden entre sí (ver
    // DeleteAlreadyDeletedKeyEvidenceBuilder/-Executor).
    public const string CommandDeleteAlreadyDeletedKey = "delete-already-deleted-key";
    // XPAY-340 — M3-T2: resuelve por key_type/key_value + customer_id (NO
    // por remote key_id) — usa los recursos Bre-B de prueba YA
    // provistos por Passport (PASSPORT_TEST_CUSTOMER_ID/
    // PASSPORT_TEST_BREB_KEY_TYPE/PASSPORT_TEST_BREB_KEY), nunca
    // PASSPORT_TEST_NEW_KEY_ID (ese es exclusivo de Suspend/Activate/
    // Delete/DeleteAlreadyDeleted, un target completamente distinto).
    public const string CommandResolveKey              = "resolve-key";
    // XPAY-344 — M3-T6 MISSING/INVALID (Create Key negativo). Comandos
    // INDEPENDIENTES entre sí y de create-key — cada uno con su propia
    // bandera de confirmación exclusiva.
    public const string CommandCreateKeyMissing = "create-key-missing";
    public const string CommandCreateKeyInvalid = "create-key-invalid";
    // XPAY-351 — M4-T1: comando inequívoco, independiente de todos los
    // comandos de M3 (create-key incluido) aunque ambos sean "creación" de
    // un recurso distinto (Key vs. QR Code).
    public const string CommandCreateQrStatic = "create-qr-static";
    // XPAY-465 — M4-T2: comando inequívoco, independiente de create-qr-static
    // (M4-T1) aunque ambos operen sobre QR codes vía el mismo IPassportQrClient.
    public const string CommandDecodeQrStatic = "decode-qr-static";
    // XPAY-471 — M4-T3, tres comandos independientes (nombres finales,
    // coherentes con la convención kebab-case ya establecida).
    public const string CommandCreateQrStaticSuspendedKey    = "create-qr-static-suspended-key";
    public const string CommandCreateQrStaticDeletedKey      = "create-qr-static-deleted-key";
    public const string CommandCreateQrStaticInvalidCustomer = "create-qr-static-invalid-customer";
    // XPAY-474 — preparación de la llave desechable de M4-T3-A.
    public const string CommandCreateM4T3SuspendedFixtureKey = "create-m4-t3-suspended-fixture-key";
    public const string CommandSuspendM4T3FixtureKey         = "suspend-m4-t3-fixture-key";
    // XPAY-489 — recuperación read-only del key_id DELETED del ciclo M3.
    public const string CommandRecoverDeletedKeyId            = "recover-deleted-key-id";
    // XPAY-492 — preparación del fixture desechable de M4-T3-B.
    public const string CommandCreateM4T3DeletedFixtureKey = "create-m4-t3-deleted-fixture-key";
    public const string CommandDeleteM4T3FixtureKey        = "delete-m4-t3-fixture-key";
    // XPAY-506 — M4-T4: Create QR Code DINÁMICO.
    public const string CommandCreateQrDynamic = "create-qr-dynamic";
    // XPAY-517 — M4-T5: Decode QR Code DINÁMICO.
    public const string CommandDecodeQrDynamic = "decode-qr-dynamic";
    // XPAY-522 — M4-T6: tres subcasos negativos de QR DINÁMICO.
    public const string CommandCreateQrDynamicSuspendedKey    = "create-qr-dynamic-suspended";
    public const string CommandCreateQrDynamicDeletedKey      = "create-qr-dynamic-deleted";
    public const string CommandCreateQrDynamicInvalidCustomer = "create-qr-dynamic-invalid-customer";
    // XPAY-529 — M5: infraestructura de certificación Create/Retrieve
    // Bre-B Payment.
    public const string CommandCreateBrebPaymentCert   = "create-breb-payment-cert";
    public const string CommandRetrieveBrebPaymentCert = "retrieve-breb-payment-cert";
    // XPAY-531 — M5-T1: Create/Link Customer B + Account B.
    public const string CommandCreateM5CustomerB = "create-m5-customer-b";
    public const string CommandCreateM5AccountB  = "create-m5-account-b";
    // XPAY-545 — M5-T2-RESOLVE.
    public const string CommandResolveKeyM5T2 = "resolve-key-m5-t2";
    // XPAY-546 — transferencia LOCAL del resolution_id a env.
    public const string CommandConfigureM5PaymentResolution = "configure-m5-payment-resolution";
    // XPAY-570 — M5-T5-MISSING-AMOUNT (Create Payment negativo).
    public const string CommandCreatePaymentMissingAmount = "create-payment-missing-amount";
    // XPAY-571 — M5-T5-MISSING-ACCOUNT (Create Payment negativo).
    public const string CommandCreatePaymentMissingAccount = "create-payment-missing-account";
    // XPAY-572 — M5-T5-INVALID-RESOLUTION (Create Payment negativo, ÚNICA
    // llamada real de este trío de subcasos M5-T5).
    public const string CommandCreatePaymentInvalidResolution = "create-payment-invalid-resolution";
    // XPAY-575 — Fase 1 (Create Key) del fixture nuevo de M5-T5-D.
    public const string CommandCreateM5T5SuspendedKeyFixture = "create-m5-t5-suspended-fixture";
    // XPAY-576 — Fase 2 (Suspend Key) del mismo fixture.
    public const string CommandSuspendM5T5FixtureKey = "suspend-m5-t5-fixture";
    // XPAY-577 — Fase 3 (Resolve real), ejecución final de M5-T5-D.
    public const string CommandResolveM5T5SuspendedKey = "resolve-m5-t5-suspended-key";
    // XPAY-613 — Fase A (Create Key) del fixture nuevo de M5-T5-E.
    public const string CommandCreateM5T5DeletedKeyFixture = "create-m5-t5-deleted-fixture";
    // XPAY-613 — Fase B (Delete Key, irreversible) del mismo fixture.
    public const string CommandDeleteM5T5FixtureKey = "delete-m5-t5-fixture";
    // XPAY-613 — Fase C (Resolve real), ejecución final de M5-T5-E.
    public const string CommandResolveM5T5DeletedKey = "resolve-m5-t5-deleted-key";
    // XPAY-619 — M5-T5-C (exceso de límite de monto).
    public const string CommandCreatePaymentOverLimit = "create-payment-over-limit";

    public const string FlagExecute              = "--execute";
    public const string FlagConfirmCreateCustomer = "--confirm-create-customer";
    // XPAY-325/326/332/334/336/340 — cada comando mutante (o, en el caso
    // de Resolve, cada comando que realiza una llamada real aunque sea
    // read-only) tiene su PROPIA bandera de confirmación, deliberadamente
    // distinta de las demás: evita que la confirmación de un comando
    // autorice por error la operación de otro (p. ej. --confirm-create-key
    // NUNCA debe autorizar suspend-key, ni --confirm-suspend-key/
    // --confirm-activate-key/--confirm-delete-key autorizar
    // delete-already-deleted-key o resolve-key, y viceversa) — "una sola
    // bandera genérica no es suficiente".
    public const string FlagConfirmCreateKey               = "--confirm-create-key";
    public const string FlagConfirmSuspendKey              = "--confirm-suspend-key";
    public const string FlagConfirmActivateKey             = "--confirm-activate-key";
    public const string FlagConfirmDeleteKey               = "--confirm-delete-key";
    public const string FlagConfirmDeleteAlreadyDeletedKey = "--confirm-delete-already-deleted-key";
    public const string FlagConfirmResolveKey              = "--confirm-resolve-key";
    public const string FlagConfirmCreateKeyMissing = "--confirm-create-key-missing";
    public const string FlagConfirmCreateKeyInvalid = "--confirm-create-key-invalid";
    // XPAY-351 — exclusiva de create-qr-static; ninguna bandera de
    // confirmación de M3 autoriza este comando, y ésta no autoriza ningún
    // comando de M3 (mismo criterio ya aplicado a todas las anteriores).
    public const string FlagConfirmCreateQrStatic = "--confirm-create-qr-static";
    // XPAY-465 — exclusiva de decode-qr-static; --confirm-create-qr-static
    // NUNCA la autoriza, y ésta NUNCA autoriza create-qr-static.
    public const string FlagConfirmDecodeQrStatic = "--confirm-decode-qr-static";
    // XPAY-471 — M4-T3, tres banderas de confirmación exclusivas. Ninguna
    // de las tres autoriza create-qr-static/decode-qr-static ni entre sí,
    // y viceversa.
    public const string FlagConfirmCreateQrStaticSuspendedKey    = "--confirm-create-qr-static-suspended-key";
    public const string FlagConfirmCreateQrStaticDeletedKey      = "--confirm-create-qr-static-deleted-key";
    public const string FlagConfirmCreateQrStaticInvalidCustomer = "--confirm-create-qr-static-invalid-customer";
    // XPAY-474 — exclusivas de la preparación de M4-T3-A; ninguna autoriza
    // create-key/suspend-key (M3), create-qr-static-suspended-key (M4-T3-A
    // final), ni entre sí.
    public const string FlagConfirmCreateM4T3SuspendedFixtureKey = "--confirm-create-m4-t3-suspended-fixture-key";
    public const string FlagConfirmSuspendM4T3FixtureKey         = "--confirm-suspend-m4-t3-fixture-key";
    // XPAY-489 — exclusiva de recover-deleted-key-id; no autoriza ni es
    // autorizada por ninguna otra bandera de confirmación.
    public const string FlagConfirmRecoverDeletedKeyId = "--confirm-recover-deleted-key";
    // XPAY-492 — exclusivas de la preparación del fixture desechable de
    // M4-T3-B; ninguna autoriza ni es autorizada por ninguna otra bandera.
    public const string FlagConfirmCreateM4T3DeletedFixtureKey = "--confirm-create-m4-t3-deleted-fixture-key";
    public const string FlagConfirmDeleteM4T3FixtureKey        = "--confirm-delete-m4-t3-fixture-key";
    // XPAY-506 — exclusiva de create-qr-dynamic; no autoriza ni es
    // autorizada por ninguna otra bandera de confirmación (incluida
    // --confirm-create-qr-static).
    public const string FlagConfirmCreateQrDynamic = "--confirm-create-qr-dynamic";
    // XPAY-517 — exclusiva de decode-qr-dynamic; no autoriza ni es
    // autorizada por ninguna otra bandera de confirmación (incluida
    // --confirm-create-qr-dynamic/--confirm-decode-qr-static).
    public const string FlagConfirmDecodeQrDynamic = "--confirm-decode-qr-dynamic";
    // XPAY-522 — tres banderas exclusivas de M4-T6, ninguna autoriza a
    // ninguna otra (ni entre sí, ni hacia M4-T3/M4-T4/M4-T5), y viceversa.
    public const string FlagConfirmCreateQrDynamicSuspendedKey    = "--confirm-create-qr-dynamic-suspended";
    public const string FlagConfirmCreateQrDynamicDeletedKey      = "--confirm-create-qr-dynamic-deleted";
    public const string FlagConfirmCreateQrDynamicInvalidCustomer = "--confirm-create-qr-dynamic-invalid-customer";
    // XPAY-529 — exclusivas de M5; ninguna autoriza ni es autorizada por
    // ninguna otra bandera de confirmación (incluidas las de M1-M4).
    public const string FlagConfirmCreateBrebPaymentCert   = "--confirm-create-breb-payment-cert";
    public const string FlagConfirmRetrieveBrebPaymentCert = "--confirm-retrieve-breb-payment-cert";
    // XPAY-531 — exclusivas de M5-T1 Customer B/Account B; ninguna autoriza
    // ni es autorizada por ninguna otra bandera de confirmación (incluidas
    // las de M5 Payment).
    public const string FlagConfirmCreateM5CustomerB = "--confirm-create-m5-customer-b";
    public const string FlagConfirmCreateM5AccountB  = "--confirm-create-m5-account-b";
    // XPAY-545 — exclusiva de M5-T2-RESOLVE; ninguna otra bandera de
    // confirmación la autoriza, incluida --confirm-resolve-key (M3-T2).
    public const string FlagConfirmResolveKeyM5T2 = "--confirm-resolve-key-m5-t2";
    // XPAY-546 — exclusiva de configure-m5-payment-resolution.
    public const string FlagConfirmConfigureM5PaymentResolution = "--confirm-configure-m5-payment-resolution";
    // XPAY-570 — exclusiva de create-payment-missing-amount.
    public const string FlagConfirmCreatePaymentMissingAmount = "--confirm-create-payment-missing-amount";
    // XPAY-571 — exclusiva de create-payment-missing-account.
    public const string FlagConfirmCreatePaymentMissingAccount = "--confirm-create-payment-missing-account";
    // XPAY-572 — exclusiva de create-payment-invalid-resolution.
    public const string FlagConfirmCreatePaymentInvalidResolution = "--confirm-create-payment-invalid-resolution";
    // XPAY-575 — exclusiva de create-m5-t5-suspended-fixture.
    public const string FlagConfirmCreateM5T5SuspendedKeyFixture = "--confirm-create-m5-t5-suspended-fixture";
    // XPAY-576 — exclusiva de suspend-m5-t5-fixture.
    public const string FlagConfirmSuspendM5T5FixtureKey = "--confirm-suspend-m5-t5-fixture";
    // XPAY-577 — exclusiva de resolve-m5-t5-suspended-key.
    public const string FlagConfirmResolveM5T5SuspendedKey = "--confirm-resolve-m5-t5-suspended-key";
    // XPAY-613 — exclusiva de create-m5-t5-deleted-fixture.
    public const string FlagConfirmCreateM5T5DeletedKeyFixture = "--confirm-create-m5-t5-deleted-fixture";
    // XPAY-613 — exclusiva de delete-m5-t5-fixture.
    public const string FlagConfirmDeleteM5T5FixtureKey = "--confirm-delete-m5-t5-fixture";
    // XPAY-613 — exclusiva de resolve-m5-t5-deleted-key.
    public const string FlagConfirmResolveM5T5DeletedKey = "--confirm-resolve-m5-t5-deleted-key";
    // XPAY-619 — exclusiva de create-payment-over-limit. Ninguna otra flag
    // del harness autoriza este caso, y ésta no autoriza ningún otro.
    public const string FlagConfirmCreatePaymentOverLimit = "--confirm-create-payment-over-limit";

    public static HarnessCommand ParseCommand(string[] args) =>
        args.Length == 0 ? HarnessCommand.Unknown :
        args[0] switch
        {
            CommandCreateCustomer          => HarnessCommand.CreateCustomer,
            CommandCreateKey               => HarnessCommand.CreateKey,
            CommandSuspendKey              => HarnessCommand.SuspendKey,
            CommandActivateKey             => HarnessCommand.ActivateKey,
            CommandDeleteKey               => HarnessCommand.DeleteKey,
            CommandDeleteAlreadyDeletedKey => HarnessCommand.DeleteAlreadyDeletedKey,
            CommandResolveKey              => HarnessCommand.ResolveKey,
            CommandCreateKeyMissing        => HarnessCommand.CreateKeyMissing,
            CommandCreateKeyInvalid        => HarnessCommand.CreateKeyInvalid,
            CommandCreateQrStatic          => HarnessCommand.CreateQrStatic,
            CommandDecodeQrStatic          => HarnessCommand.DecodeQrStatic,
            CommandCreateQrStaticSuspendedKey    => HarnessCommand.CreateQrStaticSuspendedKey,
            CommandCreateQrStaticDeletedKey      => HarnessCommand.CreateQrStaticDeletedKey,
            CommandCreateQrStaticInvalidCustomer => HarnessCommand.CreateQrStaticInvalidCustomer,
            CommandCreateM4T3SuspendedFixtureKey => HarnessCommand.CreateM4T3SuspendedFixtureKey,
            CommandSuspendM4T3FixtureKey         => HarnessCommand.SuspendM4T3FixtureKey,
            CommandRecoverDeletedKeyId           => HarnessCommand.RecoverDeletedKeyId,
            CommandCreateM4T3DeletedFixtureKey   => HarnessCommand.CreateM4T3DeletedFixtureKey,
            CommandDeleteM4T3FixtureKey          => HarnessCommand.DeleteM4T3FixtureKey,
            CommandCreateQrDynamic               => HarnessCommand.CreateQrDynamic,
            CommandDecodeQrDynamic               => HarnessCommand.DecodeQrDynamic,
            CommandCreateQrDynamicSuspendedKey    => HarnessCommand.CreateQrDynamicSuspendedKey,
            CommandCreateQrDynamicDeletedKey      => HarnessCommand.CreateQrDynamicDeletedKey,
            CommandCreateQrDynamicInvalidCustomer => HarnessCommand.CreateQrDynamicInvalidCustomer,
            CommandCreateBrebPaymentCert   => HarnessCommand.CreateBrebPaymentCert,
            CommandRetrieveBrebPaymentCert => HarnessCommand.RetrieveBrebPaymentCert,
            CommandCreateM5CustomerB => HarnessCommand.CreateM5CustomerB,
            CommandCreateM5AccountB  => HarnessCommand.CreateM5AccountB,
            CommandResolveKeyM5T2    => HarnessCommand.ResolveKeyM5T2,
            CommandConfigureM5PaymentResolution => HarnessCommand.ConfigureM5PaymentResolution,
            CommandCreatePaymentMissingAmount   => HarnessCommand.CreatePaymentMissingAmount,
            CommandCreatePaymentMissingAccount  => HarnessCommand.CreatePaymentMissingAccount,
            CommandCreatePaymentInvalidResolution => HarnessCommand.CreatePaymentInvalidResolution,
            CommandCreateM5T5SuspendedKeyFixture  => HarnessCommand.CreateM5T5SuspendedKeyFixture,
            CommandSuspendM5T5FixtureKey          => HarnessCommand.SuspendM5T5FixtureKey,
            CommandResolveM5T5SuspendedKey        => HarnessCommand.ResolveM5T5SuspendedKey,
            CommandCreateM5T5DeletedKeyFixture    => HarnessCommand.CreateM5T5DeletedKeyFixture,
            CommandDeleteM5T5FixtureKey           => HarnessCommand.DeleteM5T5FixtureKey,
            CommandResolveM5T5DeletedKey          => HarnessCommand.ResolveM5T5DeletedKey,
            CommandCreatePaymentOverLimit         => HarnessCommand.CreatePaymentOverLimit,
            _                              => HarnessCommand.Unknown,
        };

    // XPAY-312 FASE 3/6/8, extendido en XPAY-325/326:
    //  - comando no reconocido (incluyendo sin argumentos) => ShowHelp (nunca HTTP).
    //  - comando reconocido, sin --execute => DryRun (comportamiento por
    //    defecto, nunca HTTP).
    //  - --execute presente pero SIN la bandera de confirmación específica
    //    del comando => Aborted (una sola bandera genérica no basta para una
    //    operación que muta un recurso remoto).
    //  - --execute AND <confirm-flag-del-comando> => Execute (única
    //    combinación que Program.cs/HarnessApp deben interpretar como
    //    autorización para construir el stack real y llamar a Passport;
    //    XPAY-312/325/326 NUNCA invocan esta combinación de forma automática).
    public static HarnessAction Decide(string[] args, out string? abortReason)
    {
        abortReason = null;

        var command = ParseCommand(args);
        if (command == HarnessCommand.Unknown)
            return HarnessAction.ShowHelp;

        var confirmFlag = command switch
        {
            HarnessCommand.CreateCustomer          => FlagConfirmCreateCustomer,
            HarnessCommand.CreateKey               => FlagConfirmCreateKey,
            HarnessCommand.SuspendKey              => FlagConfirmSuspendKey,
            HarnessCommand.ActivateKey             => FlagConfirmActivateKey,
            HarnessCommand.DeleteKey               => FlagConfirmDeleteKey,
            HarnessCommand.DeleteAlreadyDeletedKey => FlagConfirmDeleteAlreadyDeletedKey,
            HarnessCommand.ResolveKey              => FlagConfirmResolveKey,
            HarnessCommand.CreateKeyMissing        => FlagConfirmCreateKeyMissing,
            HarnessCommand.CreateKeyInvalid        => FlagConfirmCreateKeyInvalid,
            HarnessCommand.CreateQrStatic          => FlagConfirmCreateQrStatic,
            HarnessCommand.DecodeQrStatic          => FlagConfirmDecodeQrStatic,
            HarnessCommand.CreateQrStaticSuspendedKey    => FlagConfirmCreateQrStaticSuspendedKey,
            HarnessCommand.CreateQrStaticDeletedKey      => FlagConfirmCreateQrStaticDeletedKey,
            HarnessCommand.CreateQrStaticInvalidCustomer => FlagConfirmCreateQrStaticInvalidCustomer,
            HarnessCommand.CreateM4T3SuspendedFixtureKey => FlagConfirmCreateM4T3SuspendedFixtureKey,
            HarnessCommand.SuspendM4T3FixtureKey         => FlagConfirmSuspendM4T3FixtureKey,
            HarnessCommand.RecoverDeletedKeyId           => FlagConfirmRecoverDeletedKeyId,
            HarnessCommand.CreateM4T3DeletedFixtureKey   => FlagConfirmCreateM4T3DeletedFixtureKey,
            HarnessCommand.DeleteM4T3FixtureKey          => FlagConfirmDeleteM4T3FixtureKey,
            HarnessCommand.CreateQrDynamic                => FlagConfirmCreateQrDynamic,
            HarnessCommand.DecodeQrDynamic                => FlagConfirmDecodeQrDynamic,
            HarnessCommand.CreateQrDynamicSuspendedKey    => FlagConfirmCreateQrDynamicSuspendedKey,
            HarnessCommand.CreateQrDynamicDeletedKey      => FlagConfirmCreateQrDynamicDeletedKey,
            HarnessCommand.CreateQrDynamicInvalidCustomer => FlagConfirmCreateQrDynamicInvalidCustomer,
            HarnessCommand.CreateBrebPaymentCert   => FlagConfirmCreateBrebPaymentCert,
            HarnessCommand.RetrieveBrebPaymentCert => FlagConfirmRetrieveBrebPaymentCert,
            HarnessCommand.CreateM5CustomerB => FlagConfirmCreateM5CustomerB,
            HarnessCommand.CreateM5AccountB  => FlagConfirmCreateM5AccountB,
            HarnessCommand.ResolveKeyM5T2    => FlagConfirmResolveKeyM5T2,
            HarnessCommand.ConfigureM5PaymentResolution => FlagConfirmConfigureM5PaymentResolution,
            HarnessCommand.CreatePaymentMissingAmount   => FlagConfirmCreatePaymentMissingAmount,
            HarnessCommand.CreatePaymentMissingAccount  => FlagConfirmCreatePaymentMissingAccount,
            HarnessCommand.CreatePaymentInvalidResolution => FlagConfirmCreatePaymentInvalidResolution,
            HarnessCommand.CreateM5T5SuspendedKeyFixture  => FlagConfirmCreateM5T5SuspendedKeyFixture,
            HarnessCommand.SuspendM5T5FixtureKey          => FlagConfirmSuspendM5T5FixtureKey,
            HarnessCommand.ResolveM5T5SuspendedKey        => FlagConfirmResolveM5T5SuspendedKey,
            HarnessCommand.CreateM5T5DeletedKeyFixture    => FlagConfirmCreateM5T5DeletedKeyFixture,
            HarnessCommand.DeleteM5T5FixtureKey           => FlagConfirmDeleteM5T5FixtureKey,
            HarnessCommand.ResolveM5T5DeletedKey          => FlagConfirmResolveM5T5DeletedKey,
            HarnessCommand.CreatePaymentOverLimit         => FlagConfirmCreatePaymentOverLimit,
            _                                      => null,
        };

        var execute = args.Contains(FlagExecute, StringComparer.Ordinal);
        var confirm = confirmFlag is not null && args.Contains(confirmFlag, StringComparer.Ordinal);

        if (execute && confirm)
            return HarnessAction.Execute;

        if (execute && !confirm)
        {
            abortReason = $"{FlagExecute} requiere también {confirmFlag} " +
                           "(esta operación muta un recurso remoto; una sola bandera no es suficiente).";
            return HarnessAction.Aborted;
        }

        return HarnessAction.DryRun;
    }
}
