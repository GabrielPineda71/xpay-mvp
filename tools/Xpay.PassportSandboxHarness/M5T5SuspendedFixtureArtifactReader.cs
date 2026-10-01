using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-576 — lectura READ-ONLY del artifact privado CREATED del fixture
// nuevo de M5-T5-D (escrito por CreateM5T5SuspendedKeyFixtureExecutor,
// XPAY-575). Este módulo NUNCA escribe, nunca modifica ese artefacto —
// sólo lo abre en memoria para extraer key_id/key_value (usados como
// input de Suspend) y validar su estado.
//
// Glob ESPECÍFICO del prefijo "M5-T5-SUSPENDED-KEY-FIXTURE-CREATED-" —
// nunca coincide con "...-PREPARED-" ni "...-FAILED-" (otros estados del
// mismo fixture, XPAY-575) ni con ningún otro artifact del directorio
// compartido (M5-PAYMENT-CREATE-*, M5-T2-RESOLVE-*). Estructuralmente
// imposible de confundir con el fixture HISTÓRICO de M4-T3-A: ese fixture
// nunca tuvo private capture (causa raíz de XPAY-574) y por tanto nunca
// puede aparecer bajo este glob.
public static class M5T5SuspendedFixtureArtifactReader
{
    private const string CreatedGlob = CreateM5T5SuspendedKeyFixtureExecutor.FixtureCaseIdPrefix + "-CREATED-*.private.json";

    public enum ResolutionOutcome
    {
        Found,
        DirectoryMissing,
        NoArtifactFound,
        MultipleArtifactsFound,
        Unreadable,
    }

    public sealed record Artifact(
        [property: JsonPropertyName("purpose")]     string? Purpose,
        [property: JsonPropertyName("state")]        string? State,
        [property: JsonPropertyName("key_type")]     string? KeyType,
        [property: JsonPropertyName("key_value")]    string? KeyValue,
        [property: JsonPropertyName("key_id")]       string? KeyId,
        [property: JsonPropertyName("key_value_fingerprint")] string? KeyValueFingerprint,
        [property: JsonPropertyName("key_id_fingerprint")]    string? KeyIdFingerprint);

    // I/O acotado — exige exactamente UN artifact CREATED en el directorio
    // (mismo criterio que M5ResolveArtifactReader/M5PaymentArtifactReader —
    // ambigüedad se trata como fallo, no como decisión implícita).
    public static (ResolutionOutcome Outcome, Artifact? Artifact) Resolve(string m5CaptureDirFullPath)
    {
        if (!Directory.Exists(m5CaptureDirFullPath))
            return (ResolutionOutcome.DirectoryMissing, null);

        string[] files;
        try
        {
            files = Directory.GetFiles(m5CaptureDirFullPath, CreatedGlob);
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

    // Validación de contenido — exige estado CREATED (nunca PREPARED, que
    // indicaría un Create Key interrumpido antes de recibir respuesta) y
    // los tres campos estructurales presentes.
    public static bool IsValid(Artifact artifact) =>
        artifact.State == "CREATED"
        && artifact.KeyType == "BCODE"
        && !string.IsNullOrWhiteSpace(artifact.KeyId)
        && !string.IsNullOrWhiteSpace(artifact.KeyValue);

    // XPAY-577 — lectura READ-ONLY del marcador de consumo FIJO escrito por
    // SuspendM5T5FixtureKeyExecutor (XPAY-576) — sin duplicar la lógica de
    // I/O, extiende este mismo reader en vez de crear un tipo nuevo
    // dedicado a un único archivo de nombre fijo (nunca un glob, siempre
    // el mismo nombre exacto).
    public sealed record SuspendMarker(
        [property: JsonPropertyName("key_id_fingerprint")]    string? KeyIdFingerprint,
        [property: JsonPropertyName("key_value_fingerprint")] string? KeyValueFingerprint,
        [property: JsonPropertyName("outcome")]                string? Outcome,
        [property: JsonPropertyName("returned_status")]        string? ReturnedStatus);

    public static SuspendMarker? ResolveSuspendMarker(string m5CaptureDirFullPath)
    {
        var markerPath = Path.Combine(m5CaptureDirFullPath,
            $"{SuspendM5T5FixtureKeyExecutor.SuspendGuardCaseId}-MARKER.private.json");

        if (!File.Exists(markerPath))
            return null;

        try
        {
            var json = File.ReadAllText(markerPath);
            return JsonSerializer.Deserialize<SuspendMarker>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Correlación Create -> Suspend: mismo key_id y mismo key_value, y
    // Suspend debe haber tenido éxito Y devuelto explícitamente el estado
    // "SUSPENDED" (no basta con result=PASS — un provider podría en teoría
    // aceptar la llamada sin confirmar el estado esperado; fail-closed
    // ante cualquier ambigüedad).
    public static bool IsSuspendCorrelationValid(Artifact createdArtifact, SuspendMarker? suspendMarker) =>
        suspendMarker is not null
        && suspendMarker.Outcome == "EXPECTED_SUSPEND_SUCCESS"
        && suspendMarker.ReturnedStatus == "SUSPENDED"
        && suspendMarker.KeyIdFingerprint == createdArtifact.KeyIdFingerprint
        && suspendMarker.KeyValueFingerprint == createdArtifact.KeyValueFingerprint;
}
