namespace Xpay.PassportSandboxHarness;

// XPAY-312 FASE 9 — único formato de salida permitido para evidencia de una
// futura ejecución. Campos deliberadamente limitados a los permitidos por
// el prompt: nunca Authorization/access token/API key/secret/PII completa.
public sealed record RedactedResult(
    string Timestamp,
    string CaseId,
    string Method,
    string Path,
    string Result,      // PASS|FAIL|DRY_RUN|ABORTED
    string? Detail,      // texto saneado, nunca body/PII/secretos
    int? HttpStatus = null,
    long? DurationMs = null,
    string? RemoteIdFingerprint = null) // p.ej. hash corto, nunca el id completo salvo que ya sea opaco y no-PII
{
    public IEnumerable<string> ToLines()
    {
        yield return $"timestamp={Timestamp}";
        yield return $"case={CaseId}";
        yield return $"method={Method}";
        yield return $"path={Path}";
        if (HttpStatus is not null) yield return $"http_status={HttpStatus}";
        if (DurationMs is not null) yield return $"duration_ms={DurationMs}";
        if (RemoteIdFingerprint is not null) yield return $"remote_id_fingerprint={RemoteIdFingerprint}";
        yield return $"result={Result}";
        if (!string.IsNullOrWhiteSpace(Detail)) yield return $"detail={Detail}";
    }
}
