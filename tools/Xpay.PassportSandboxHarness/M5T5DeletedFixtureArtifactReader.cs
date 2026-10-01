using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-613 — lectura READ-ONLY de los artefactos privados del fixture
// nuevo de M5-T5-E: el registro CREATED (escrito por
// CreateM5T5DeletedKeyFixtureExecutor, Fase A) y el marcador de consumo de
// Delete (escrito por DeleteM5T5FixtureKeyExecutor, Fase B). Este módulo
// NUNCA escribe, nunca modifica esos artefactos — sólo los abre en memoria
// para extraer key_id/key_value (inputs de las Fases B y C) y validar su
// estado.
//
// Mirror ESTRUCTURAL de M5T5SuspendedFixtureArtifactReader (XPAY-576/577),
// deliberadamente un tipo SEPARADO en vez de parametrizar aquél: aquel
// reader ya está publicado y probado contra el fixture de M5-T5-D cuya
// evidencia final ya fue generada — M5-T5-E no debe arriesgar esa base
// cerrada sólo para unificar código, y dos readers independientes hacen
// estructuralmente imposible que un artefacto de D satisfaga un guard de E
// (o viceversa).
//
// Glob ESPECÍFICO del prefijo "M5-T5-DELETED-KEY-FIXTURE-CREATED-" — nunca
// coincide con "...-PREPARED-" ni "...-FAILED-" (otros estados del mismo
// fixture), ni con "M5-T5-SUSPENDED-KEY-FIXTURE-*" (M5-T5-D), ni con
// ningún otro artifact del directorio compartido (M5-PAYMENT-CREATE-*,
// M5-T2-RESOLVE-*). Estructuralmente imposible de confundir con el fixture
// HISTÓRICO de M4-T3-B: ese fixture nunca tuvo private capture y por tanto
// nunca puede aparecer bajo este glob.
public static class M5T5DeletedFixtureArtifactReader
{
    private const string CreatedGlob = CreateM5T5DeletedKeyFixtureExecutor.FixtureCaseIdPrefix + "-CREATED-*.private.json";

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
    // (mismo criterio que M5T5SuspendedFixtureArtifactReader/
    // M5ResolveArtifactReader/M5PaymentArtifactReader — la ambigüedad se
    // trata como fallo, nunca como decisión implícita).
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
    // indicaría un Create Key interrumpido antes de recibir respuesta), el
    // purpose propio de M5-T5-E (un artefacto de M5-T5-D jamás lo
    // satisface, incluso si por accidente llegara a aparecer bajo este
    // glob), y los campos estructurales presentes.
    public static bool IsValid(Artifact artifact) =>
        artifact.State == "CREATED"
        && artifact.KeyType == "BCODE"
        && artifact.Purpose == CreateM5T5DeletedKeyFixtureExecutor.FixturePurpose
        && !string.IsNullOrWhiteSpace(artifact.KeyId)
        && !string.IsNullOrWhiteSpace(artifact.KeyValue);

    // Lectura READ-ONLY del marcador de consumo FIJO escrito por
    // DeleteM5T5FixtureKeyExecutor (Fase B) — nunca un glob, siempre el
    // mismo nombre exacto.
    //
    // A DIFERENCIA del SuspendMarker de M5-T5-D (que registra
    // returned_status="SUSPENDED" leído del body de la respuesta), DELETE
    // /v1/keys/{key_id} devuelve 204 No Content SIN body: no existe ningún
    // status que leer. La confirmación del proveedor es el propio 204, que
    // la Fase B registra como delete_acknowledged=true.
    public sealed record DeleteMarker(
        [property: JsonPropertyName("key_id_fingerprint")]    string? KeyIdFingerprint,
        [property: JsonPropertyName("key_value_fingerprint")] string? KeyValueFingerprint,
        [property: JsonPropertyName("outcome")]                string? Outcome,
        [property: JsonPropertyName("delete_acknowledged")]   bool? DeleteAcknowledged);

    public static DeleteMarker? ResolveDeleteMarker(string m5CaptureDirFullPath)
    {
        var markerPath = Path.Combine(m5CaptureDirFullPath,
            $"{DeleteM5T5FixtureKeyExecutor.DeleteGuardCaseId}-MARKER.private.json");

        if (!File.Exists(markerPath))
            return null;

        try
        {
            var json = File.ReadAllText(markerPath);
            return JsonSerializer.Deserialize<DeleteMarker>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Correlación Create -> Delete: mismo key_id y mismo key_value, y
    // Delete debe haber tenido éxito Y haber sido reconocido
    // explícitamente por el proveedor (204 No Content). No basta con
    // result=PASS — fail-closed ante cualquier ambigüedad, mismo criterio
    // que IsSuspendCorrelationValid en M5-T5-D.
    public static bool IsDeleteCorrelationValid(Artifact createdArtifact, DeleteMarker? deleteMarker) =>
        deleteMarker is not null
        && deleteMarker.Outcome == "EXPECTED_DELETE_SUCCESS"
        && deleteMarker.DeleteAcknowledged == true
        && deleteMarker.KeyIdFingerprint == createdArtifact.KeyIdFingerprint
        && deleteMarker.KeyValueFingerprint == createdArtifact.KeyValueFingerprint;
}
