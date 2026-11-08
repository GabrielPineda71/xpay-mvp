using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-517 — lectura READ-ONLY del artefacto privado M4-T4 (escrito por
// CreateQrDynamicExecutor/M4T4PrivateCapture, XPAY-508) desde M4-T5. Este
// módulo NUNCA escribe en el directorio de M4-T4, nunca modifica ese
// artefacto, nunca lo copia a ningún otro lugar — sólo lo abre en memoria
// para extraer qr_code_data/qr_code_reference_received (usados como input
// de Decode) y los campos de identidad (case_id/status/type/fingerprints).
public static class M4T4ArtifactReader
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
        [property: JsonPropertyName("case_id")]                   string? CaseId,
        [property: JsonPropertyName("backend_commit_sha")]        string? BackendCommitSha,
        [property: JsonPropertyName("key_id_fingerprint")]        string? KeyIdFingerprint,
        [property: JsonPropertyName("customer_id_fingerprint")]   string? CustomerIdFingerprint,
        [property: JsonPropertyName("status")]                    string? Status,
        [property: JsonPropertyName("type")]                      string? Type,
        [property: JsonPropertyName("qr_code_reference_received")] string? QrCodeReferenceReceived,
        [property: JsonPropertyName("qr_code_data")]              string? QrCodeData);

    // I/O acotado — exige exactamente UN artefacto ".private.json" en el
    // directorio (nunca elige "el más reciente" silenciosamente entre
    // varios — ambigüedad se trata como fallo, no como decisión implícita).
    public static (ResolutionOutcome Outcome, Artifact? Artifact) Resolve(string m4t4CaptureDirFullPath)
    {
        if (!Directory.Exists(m4t4CaptureDirFullPath))
            return (ResolutionOutcome.DirectoryMissing, null);

        string[] files;
        try
        {
            files = Directory.GetFiles(m4t4CaptureDirFullPath, "*.private.json");
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

    // Identidad esperada del SUCCESS M4-T4 (XPAY-513): case_id=M4-T4,
    // status=ACTIVE, type=DYNAMIC, más los fingerprints de key_id/
    // customer_id ya configurados para este entorno (los mismos targets
    // que create-qr-dynamic ya usa — PASSPORT_TEST_QR_KEY_ID/
    // PASSPORT_TEST_CUSTOMER_ID). Comparación PURA por fingerprint — nunca
    // compara ni expone valores crudos.
    public static bool IdentityMatches(
        Artifact artifact, string expectedKeyIdFingerprint, string expectedCustomerIdFingerprint) =>
        artifact.CaseId == "M4-T4"
        && artifact.Status == "ACTIVE"
        && artifact.Type == "DYNAMIC"
        && artifact.KeyIdFingerprint == expectedKeyIdFingerprint
        && artifact.CustomerIdFingerprint == expectedCustomerIdFingerprint;
}
