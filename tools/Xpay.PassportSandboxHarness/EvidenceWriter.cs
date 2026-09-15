using System.Text.Json;

namespace Xpay.PassportSandboxHarness;

// XPAY-325 — escritura segura del expediente de evidencia. Diseño:
//
//   {baseDirectory}/{case_id}/evidence-{executed_at_utc saneado}.json
//
// El nombre de archivo incluye el timestamp de ejecución (no un nombre fijo
// "evidence.json") deliberadamente: permite múltiples ejecuciones legítimas
// del mismo caso (p. ej. un retry documentado) sin colisionar, mientras
// sigue siendo imposible sobrescribir SILENCIOSAMENTE una evidencia
// existente — dos ejecuciones con el mismo timestamp exacto (a resolución
// de segundo) seguirían fallando explícitamente en vez de sobrescribirse.
// Esta es la "estrategia segura documentada" prevista en XPAY-325 FASE 11;
// se documenta también en README.md del expediente.
//
// Escritura atómica: se escribe primero a un archivo temporal único
// (sufijo GUID) y luego se hace File.Move al nombre final — File.Move es
// atómico dentro del mismo volumen en los sistemas de archivos habituales
// (APFS/ext4/NTFS), evitando que un fallo a mitad de escritura deje un
// evidence.json parcial y potencialmente engañoso. Si el Move falla, el
// temporal se limpia explícitamente.
public static class EvidenceWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public static string Write(string baseDirectory, EvidenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        // XPAY-325 FASE 8 — nunca se permite evidencia sin backend_commit_sha.
        if (string.IsNullOrWhiteSpace(record.BackendCommitSha))
            throw new ArgumentException(
                "backend_commit_sha no puede estar vacío — nunca se genera evidencia sin él.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.CaseId))
            throw new ArgumentException("case_id es requerido.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.ExecutedAtUtc))
            throw new ArgumentException("executed_at_utc es requerido.", nameof(record));
        if (record.Result != EvidenceRecord.ResultPass && record.Result != EvidenceRecord.ResultFail)
            throw new ArgumentException(
                $"result debe ser \"{EvidenceRecord.ResultPass}\" o \"{EvidenceRecord.ResultFail}\".", nameof(record));

        var caseDir = Path.Combine(baseDirectory, SanitizeForPath(record.CaseId));
        Directory.CreateDirectory(caseDir);

        var fileName = $"evidence-{SanitizeForPath(record.ExecutedAtUtc)}.json";
        var finalPath = Path.Combine(caseDir, fileName);

        if (File.Exists(finalPath))
            throw new InvalidOperationException(
                $"Ya existe evidencia para {record.CaseId} en '{finalPath}' — no se sobrescribe silenciosamente. " +
                "Si esto es un re-intento legítimo, se generará con un nuevo executed_at_utc.");

        var json = JsonSerializer.Serialize(record, JsonOpts);

        var tempPath = finalPath + $".tmp-{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, json);
        try
        {
            File.Move(tempPath, finalPath);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }

        return finalPath;
    }

    private static string SanitizeForPath(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(chars);
    }
}
