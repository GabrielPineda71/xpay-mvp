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

// XPAY-325/326/332/334 — comando reconocido por el harness. Se generaliza
// desde el único comando "create-customer" de XPAY-312 para soportar
// múltiples casos de certificación sin duplicar la lógica de parsing/guards.
public enum HarnessCommand
{
    Unknown,
    CreateCustomer,
    CreateKey,
    SuspendKey,
    ActivateKey,
    DeleteKey,
}

public static class HarnessDecision
{
    public const string CommandCreateCustomer = "create-customer";
    public const string CommandCreateKey      = "create-key";
    public const string CommandSuspendKey     = "suspend-key";
    public const string CommandActivateKey    = "activate-key";
    public const string CommandDeleteKey      = "delete-key";

    public const string FlagExecute              = "--execute";
    public const string FlagConfirmCreateCustomer = "--confirm-create-customer";
    // XPAY-325/326/332/334 — cada comando mutante tiene su PROPIA bandera
    // de confirmación, deliberadamente distinta de las demás: evita que la
    // confirmación de un comando autorice por error la mutación de otro
    // (p. ej. --confirm-create-key NUNCA debe autorizar suspend-key, ni
    // --confirm-suspend-key/--confirm-activate-key autorizar delete-key, y
    // viceversa) — "una sola bandera genérica no es suficiente".
    public const string FlagConfirmCreateKey   = "--confirm-create-key";
    public const string FlagConfirmSuspendKey  = "--confirm-suspend-key";
    public const string FlagConfirmActivateKey = "--confirm-activate-key";
    public const string FlagConfirmDeleteKey   = "--confirm-delete-key";

    public static HarnessCommand ParseCommand(string[] args) =>
        args.Length == 0 ? HarnessCommand.Unknown :
        args[0] switch
        {
            CommandCreateCustomer => HarnessCommand.CreateCustomer,
            CommandCreateKey       => HarnessCommand.CreateKey,
            CommandSuspendKey      => HarnessCommand.SuspendKey,
            CommandActivateKey     => HarnessCommand.ActivateKey,
            CommandDeleteKey       => HarnessCommand.DeleteKey,
            _                      => HarnessCommand.Unknown,
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
            HarnessCommand.CreateCustomer => FlagConfirmCreateCustomer,
            HarnessCommand.CreateKey      => FlagConfirmCreateKey,
            HarnessCommand.SuspendKey     => FlagConfirmSuspendKey,
            HarnessCommand.ActivateKey    => FlagConfirmActivateKey,
            HarnessCommand.DeleteKey      => FlagConfirmDeleteKey,
            _                             => null,
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
