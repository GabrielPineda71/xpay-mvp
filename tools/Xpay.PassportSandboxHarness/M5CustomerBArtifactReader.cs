using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-531 — lectura READ-ONLY del artefacto privado M5-T1-A (Customer B),
// escrito por CreateM5CustomerBExecutor/M5CustomerBPrivateCapture, para que
// M5-T1-B (Account B) obtenga customer_id EXCLUSIVAMENTE de aquí — nunca de
// un argumento CLI, nunca de un valor hardcodeado, nunca del Customer A ni
// de un customer histórico. Este módulo NUNCA escribe, sólo abre el
// artefacto en memoria.
public static class M5CustomerBArtifactReader
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
        [property: JsonPropertyName("customer_id")]         string? CustomerId);

    // I/O acotado — exige exactamente UN artefacto ".private.json" en el
    // directorio (mismo criterio que M4T4ArtifactReader/M5PaymentArtifactReader
    // — ambigüedad se trata como fallo, no como decisión implícita).
    public static (ResolutionOutcome Outcome, Artifact? Artifact) Resolve(string customerBCaptureDirFullPath)
    {
        if (!Directory.Exists(customerBCaptureDirFullPath))
            return (ResolutionOutcome.DirectoryMissing, null);

        string[] files;
        try
        {
            files = Directory.GetFiles(customerBCaptureDirFullPath, "*.private.json");
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

    // Identidad esperada del artefacto Customer B: case_id="M5-T1-A" y un
    // customer_id crudo no vacío. No hay un fingerprint "esperado" externo
    // con el que comparar (a diferencia de M4T4→M4T5, que sí validaba contra
    // un target previamente configurado) — aquí la única fuente de verdad
    // es el propio artefacto de M5-T1-A.
    public static bool IsValid(Artifact artifact) =>
        artifact.CaseId == "M5-T1-A" && !string.IsNullOrWhiteSpace(artifact.CustomerId);
}
