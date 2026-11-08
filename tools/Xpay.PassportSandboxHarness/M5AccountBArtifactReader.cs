using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-533 — lectura READ-ONLY del artefacto privado M5-T1-B (Account B),
// escrito por CreateM5AccountBExecutor/M5AccountBPrivateCapture. A
// diferencia de M5CustomerBArtifactReader (usado tanto por Account B, como
// consumidor, como — desde XPAY-533 — por el propio Customer B como
// AUTO-chequeo de repetición), este módulo es consumido EXCLUSIVAMENTE por
// CreateM5AccountBExecutor sobre su PROPIO directorio de captura, como
// no-repeat guard: antes de llamar a LinkAccountAsync, verifica si YA
// existe un artefacto M5-T1-B válido y, de ser así, bloquea localmente sin
// tocar Passport. Este módulo NUNCA escribe.
public static class M5AccountBArtifactReader
{
    public enum ResolutionOutcome
    {
        Found,
        DirectoryMissing,
        NoArtifactFound,
        MultipleArtifactsFound,
        Unreadable,
    }

    public sealed record Artifact(
        [property: JsonPropertyName("case_id")]            string? CaseId,
        [property: JsonPropertyName("backend_commit_sha")] string? BackendCommitSha,
        [property: JsonPropertyName("customer_id")]         string? CustomerId,
        [property: JsonPropertyName("account_id")]          string? AccountId);

    // I/O acotado — exige exactamente UN artefacto ".private.json" en el
    // directorio (mismo criterio que M4T4ArtifactReader/M5PaymentArtifactReader/
    // M5CustomerBArtifactReader — ambigüedad se trata como fallo, no como
    // decisión implícita). NUNCA confunde el archivo de lock
    // (m5-t1-b.execution.lock) con un artefacto — el glob "*.private.json"
    // no lo incluye.
    public static (ResolutionOutcome Outcome, Artifact? Artifact) Resolve(string accountBCaptureDirFullPath)
    {
        if (!Directory.Exists(accountBCaptureDirFullPath))
            return (ResolutionOutcome.DirectoryMissing, null);

        string[] files;
        try
        {
            files = Directory.GetFiles(accountBCaptureDirFullPath, "*.private.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (ResolutionOutcome.Unreadable, null);
        }

        if (files.Length == 0)
            return (ResolutionOutcome.NoArtifactFound, null);
        if (files.Length > 1)
            return (ResolutionOutcome.MultipleArtifactsFound, null);

        try
        {
            var json = File.ReadAllText(files[0]);
            var artifact = JsonSerializer.Deserialize<Artifact>(json);
            return artifact is null
                ? (ResolutionOutcome.Unreadable, null)
                : (ResolutionOutcome.Found, artifact);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (ResolutionOutcome.Unreadable, null);
        }
    }

    // Identidad esperada del artefacto Account B: case_id="M5-T1-B" y un
    // account_id crudo no vacío. El formato privado actual de Account B
    // (ver CreateM5AccountBExecutor.TryWritePrivateArtifact) SÓLO se
    // escribe DESPUÉS de recibir una respuesta 2xx con id no vacío de
    // Passport (RequireAccountId ya lo garantiza aguas arriba) — por lo
    // tanto CUALQUIER artefacto que pase este chequeo representa,
    // inequívocamente, una ejecución previa ACEPTADA por Passport; el
    // formato actual no puede producir un artefacto que represente un
    // intento fallido o no aceptado (XPAY-533 §3.F).
    public static bool IsValid(Artifact artifact) =>
        artifact.CaseId == "M5-T1-B" && !string.IsNullOrWhiteSpace(artifact.AccountId);
}
