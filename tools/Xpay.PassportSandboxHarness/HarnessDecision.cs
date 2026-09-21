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
