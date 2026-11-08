using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-529 — lectura READ-ONLY del artefacto privado M5 Create Payment
// (escrito por CreateBrebPaymentCertificationExecutor/M5PaymentPrivateCapture)
// desde el futuro Retrieve. Este módulo NUNCA escribe, nunca modifica ese
// artefacto — sólo lo abre en memoria para extraer payment_id (usado como
// input de Retrieve) y los campos de identidad (case_id/account_id
// fingerprint/backend_commit_sha).
//
// XPAY-551 — CORRECCIÓN: el directorio de captura privada
// (M5PaymentPrivateCapture.EnvPrivateCaptureDir) pasó a ser COMPARTIDO
// con M5-T2-RESOLVE desde XPAY-545 (reutilizado a propósito, sin crear
// una variable de entorno nueva). El glob original, genérico
// ("*.private.json"), interpretaba los artefactos
// "M5-T2-RESOLVE-*.private.json"/"M5-T2-RESOLVE-CONSUMED-*.private.json"
// como candidatos de Create Payment, produciendo un falso
// MultipleArtifactsFound apenas ambos tipos de artefacto coexistían
// (confirmado en producción real: XPAY-547/548/549 dejaron artefactos
// Resolve reales en el directorio; XPAY-550 lo descubrió al bloquear
// LOCAL_BLOCKED antes de cualquier HTTP). El glob ahora es ESPECÍFICO del
// prefijo de caso ("M5-PAYMENT-CREATE-*.private.json") — mismo criterio
// ya usado por M5ResolveArtifactReader (XPAY-545) para el mismo problema
// en sentido inverso. El artefacto real ya escrito por XPAY-549 sigue
// siendo válido sin ninguna reescritura: su nombre de archivo ya
// empezaba con "M5-PAYMENT-CREATE-" desde el principio (mismo patrón
// `{caseId}-{timestamp}.private.json` de M5PaymentPrivateCapture.Write).
public static class M5PaymentArtifactReader
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
        [property: JsonPropertyName("case_id")]                 string? CaseId,
        [property: JsonPropertyName("backend_commit_sha")]      string? BackendCommitSha,
        [property: JsonPropertyName("account_id_fingerprint")]  string? AccountIdFingerprint,
        [property: JsonPropertyName("payment_id")]              string? PaymentId);

    // Glob ESPECÍFICO del prefijo de caso — nunca genérico (XPAY-551).
    // Naturalmente excluye cualquier artefacto de M5-T2-RESOLVE (y su
    // marcador de consumo) que coexista en el mismo directorio
    // compartido, sin necesitar ningún filtro de exclusión adicional
    // (a diferencia de M5ResolveArtifactReader, que sí necesita excluir
    // su propio marcador "...CONSUMED-" porque comparte el mismo prefijo
    // de caso que su artefacto base).
    private const string PaymentCreateGlob = CreateBrebPaymentCertificationEvidenceBuilder.CaseId + "-*.private.json";

    // I/O acotado — exige exactamente UN artefacto privado de Create
    // Payment en el directorio (mismo criterio que M4T4ArtifactReader/
    // M5ResolveArtifactReader — ambigüedad se trata como fallo, no como
    // decisión implícita).
    public static (ResolutionOutcome Outcome, Artifact? Artifact) Resolve(string m5CaptureDirFullPath)
    {
        if (!Directory.Exists(m5CaptureDirFullPath))
            return (ResolutionOutcome.DirectoryMissing, null);

        string[] files;
        try
        {
            files = Directory.GetFiles(m5CaptureDirFullPath, PaymentCreateGlob);
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

    // Identidad esperada del artefacto Create Payment M5: case_id="M5-PAYMENT-CREATE",
    // más el fingerprint de account_id ya configurado para este entorno
    // (el mismo target que Create Payment ya usó). Comparación PURA por
    // fingerprint — nunca compara ni expone valores crudos.
    public static bool IdentityMatches(Artifact artifact, string expectedAccountIdFingerprint) =>
        artifact.CaseId == CreateBrebPaymentCertificationEvidenceBuilder.CaseId
        && artifact.AccountIdFingerprint == expectedAccountIdFingerprint
        && !string.IsNullOrWhiteSpace(artifact.PaymentId);
}
