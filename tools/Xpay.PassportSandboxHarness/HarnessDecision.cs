namespace Xpay.PassportSandboxHarness;

// XPAY-312 — decisión de acción del harness a partir de los argumentos de
// línea de comandos ÚNICAMENTE (función pura, sin I/O, sin red — testeable
// offline). El comportamiento por defecto es SIEMPRE seguro: sin argumentos,
// o sin AMBAS banderas de mutación, nunca se llega a Execute.
public enum HarnessAction
{
    ShowHelp,
    Aborted,
    DryRun,
    Execute,
}

public static class HarnessDecision
{
    public const string CommandCreateCustomer   = "create-customer";
    public const string FlagExecute             = "--execute";
    public const string FlagConfirmCreateCustomer = "--confirm-create-customer";

    // XPAY-312 FASE 3/6/8:
    //  - sin comando reconocido => ShowHelp (nunca HTTP).
    //  - comando reconocido, sin --execute => DryRun (comportamiento por
    //    defecto, nunca HTTP).
    //  - --execute presente pero SIN --confirm-create-customer => Aborted
    //    (una sola bandera genérica no basta para una operación que crea un
    //    recurso remoto — FASE 8).
    //  - --execute AND --confirm-create-customer => Execute (única
    //    combinación que Program.cs debe interpretar como autorización para
    //    construir el stack real y llamar a Passport; XPAY-312 NUNCA invoca
    //    esta combinación).
    public static HarnessAction Decide(string[] args, out string? abortReason)
    {
        abortReason = null;

        if (args.Length == 0 || args[0] != CommandCreateCustomer)
            return HarnessAction.ShowHelp;

        var execute = args.Contains(FlagExecute, StringComparer.Ordinal);
        var confirm = args.Contains(FlagConfirmCreateCustomer, StringComparer.Ordinal);

        if (execute && confirm)
            return HarnessAction.Execute;

        if (execute && !confirm)
        {
            abortReason = $"{FlagExecute} requiere también {FlagConfirmCreateCustomer} " +
                           "(create-customer crea un recurso remoto; una sola bandera no es suficiente).";
            return HarnessAction.Aborted;
        }

        return HarnessAction.DryRun;
    }
}
