using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-545 — lectura READ-ONLY de los artefactos privados de M5-T2 Resolve,
// dentro del directorio COMPARTIDO ya configurado por
// M5PaymentPrivateCapture.EnvPrivateCaptureDir (XPAY-545 §3 — reutilizado
// deliberadamente, sin crear una variable de entorno nueva).
//
// IMPORTANTE — a diferencia de M5CustomerBArtifactReader/
// M5AccountBArtifactReader (cada uno con directorio DEDICADO, donde un
// glob genérico "*.private.json" era seguro), este directorio es
// COMPARTIDO con los artefactos de Create/Retrieve Payment (case_id
// "M5-PAYMENT-CREATE"/"M5-PAYMENT-RETRIEVE"). Por eso el glob aquí es
// ESPECÍFICO del prefijo de caso ("M5-T2-RESOLVE-*.private.json"), nunca
// genérico — evita falsos positivos de "múltiples artefactos" cuando en
// realidad son de casos distintos.
//
// Dos tipos de archivo conviven bajo ese prefijo:
//   - "M5-T2-RESOLVE-{timestamp}.private.json"          — el artefacto
//     real (resolution_id crudo + metadata de vigencia).
//   - "M5-T2-RESOLVE-CONSUMED-{timestamp}.private.json" — marcador de
//     consumo, escrito por M5ResolveEnvTransfer tras transferir el
//     resolution_id a PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID. NUNCA
//     modifica el artefacto original (mismo principio de escritura
//     atómica "nunca sobrescribir" ya aplicado en todo el proyecto) — es
//     un archivo NUEVO y separado que referencia al original por su
//     mismo sufijo de timestamp.
public static class M5ResolveArtifactReader
{
    public const string CaseId         = "M5-T2-RESOLVE";
    public const string ConsumedPrefix = "M5-T2-RESOLVE-CONSUMED-";
    private const string ResolveGlob   = "M5-T2-RESOLVE-*.private.json";

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
        [property: JsonPropertyName("captured_at_utc")]     string? CapturedAtUtc,
        [property: JsonPropertyName("customer_id_fingerprint")] string? CustomerIdFingerprint,
        [property: JsonPropertyName("key_value_fingerprint")]   string? KeyValueFingerprint,
        [property: JsonPropertyName("resolution_id")]       string? ResolutionId,
        [property: JsonPropertyName("resolved_at_raw")]     string? ResolvedAtRaw,
        [property: JsonPropertyName("expires_at_raw")]      string? ExpiresAtRaw)
    {
        // El sufijo de timestamp del nombre de archivo — usado para
        // correlacionar con un eventual marcador "...CONSUMED-{mismo sufijo}".
        [JsonIgnore]
        public string? SourceFileNameSuffix { get; init; }
    }

    // I/O acotado — exige exactamente UN artefacto "M5-T2-RESOLVE-*.private.json"
    // (excluyendo los marcadores "...CONSUMED-*", que son un tipo de
    // archivo distinto pese a compartir el mismo prefijo de caso).
    public static (ResolutionOutcome Outcome, Artifact? Artifact) Resolve(string m5CaptureDirFullPath)
    {
        if (!Directory.Exists(m5CaptureDirFullPath))
            return (ResolutionOutcome.DirectoryMissing, null);

        string[] files;
        try
        {
            files = Directory.GetFiles(m5CaptureDirFullPath, ResolveGlob)
                .Where(f => !Path.GetFileName(f).StartsWith(ConsumedPrefix, StringComparison.Ordinal))
                .ToArray();
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
            var parsed = JsonSerializer.Deserialize<Artifact>(json);
            if (parsed is null)
                return (ResolutionOutcome.Unreadable, null);

            var fileName = Path.GetFileName(files[0]);
            var suffix = fileName.StartsWith(CaseId + "-", StringComparison.Ordinal)
                ? fileName[(CaseId.Length + 1)..]
                : fileName;
            var artifact = parsed with { SourceFileNameSuffix = suffix };
            return (ResolutionOutcome.Found, artifact);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (ResolutionOutcome.Unreadable, null);
        }
    }

    public static bool IsValid(Artifact artifact) =>
        artifact.CaseId == CaseId && !string.IsNullOrWhiteSpace(artifact.ResolutionId);

    // ¿Ya existe un marcador de consumo para ESTE artefacto específico?
    // (correlación por sufijo de timestamp del nombre de archivo — nunca
    // por contenido crudo).
    public static bool HasConsumedMarker(string m5CaptureDirFullPath, Artifact artifact)
    {
        if (string.IsNullOrEmpty(artifact.SourceFileNameSuffix))
            return false;

        var markerPath = Path.Combine(m5CaptureDirFullPath, ConsumedPrefix + artifact.SourceFileNameSuffix);
        return File.Exists(markerPath);
    }

    // XPAY-545 §6 — guard anti-repetición basado en vigencia REAL
    // (expires_at, campo que el response de Passport SÍ expone — nunca se
    // inventa una regla de expiración que el contrato no exponga).
    //
    // Bloquea (BLOCKING=true) si:
    //   - el artefacto NO fue consumido todavía (nunca se transfirió a
    //     PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID) — sin importar vigencia; o
    //   - fue consumido PERO expires_at indica que sigue vigente; o
    //   - fue consumido y expires_at está ausente/no parseable (fail-closed
    //     — nunca se asume vencido sin evidencia).
    // Permite una NUEVA ejecución real (BLOCKING=false) ÚNICAMENTE si fue
    // consumido Y expires_at es parseable Y ya venció.
    public static bool IsBlockingForRepeat(string m5CaptureDirFullPath, Artifact artifact, DateTime nowUtc)
    {
        var consumed = HasConsumedMarker(m5CaptureDirFullPath, artifact);
        if (!consumed)
            return true; // vigente/no consumido — bloquea, XPAY-545 §6.

        if (!DateTime.TryParse(
                artifact.ExpiresAtRaw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var expiresAtUtc))
        {
            return true; // sin timestamp de expiración confiable — fail-closed.
        }

        return expiresAtUtc > nowUtc; // true = todavía vigente = bloquea.
    }
}
