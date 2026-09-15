namespace Xpay.PassportSandboxHarness;

// XPAY-326 — clasificación genérica del resultado de una operación real
// sobre una llave existente (Suspend/futuro Activate/Delete). Análoga a
// CreateKeyExecutionOutcome/CreateKeyExecutionResult (XPAY-325), pero
// DELIBERADAMENTE un tipo separado en vez de reutilizar/renombrar aquel:
// CreateKeyExecutor/CreateKeyEvidenceBuilder ya están publicados y
// testeados (M3-T1 real ya ejecutado y su evidencia publicada) — XPAY-326
// no debe arriesgar esa base ya cerrada sólo para unificar nombres.
//
// LocalBlocked NUNCA produce evidencia persistible (Evidence=null): la
// operación no llegó a intentarse contra Passport (target ausente, commit
// SHA no resoluble). Success/PassportFailure SÍ representan una
// interacción remota real y su Evidence debe persistirse.
public enum KeyOperationOutcome
{
    LocalBlocked,
    Success,
    PassportFailure,
}

public sealed record KeyOperationResult(
    KeyOperationOutcome Outcome,
    EvidenceRecord? Evidence,
    string? Detail);
