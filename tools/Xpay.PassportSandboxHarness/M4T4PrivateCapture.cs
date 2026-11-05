using System.Text.Json;

namespace Xpay.PassportSandboxHarness;

// XPAY-508 — captura PRIVADA, NO versionable, del material auténtico que
// Passport devuelva en la futura ejecución real de M4-T4 (qr_code_data/
// qr_code_image crudos) — el Anexo exige como evidencia "capturas de
// pantalla del QR" y "ejemplos del payload decodificado", que un
// fingerprint SHA-256 (irreversible, ya usado en evidence.json) no puede
// producir por sí solo. Este módulo NUNCA escribe dentro de docs/ ni de
// ningún directorio versionado — su ÚNICO destino es una ruta privada,
// fuera del repositorio, configurada explícitamente por el operador (opt-in
// obligatorio, XPAY-508 §6).
//
// Separación estricta de responsabilidades (XPAY-508 §3):
//   A) evidence.json (EvidenceWriter/CreateQrDynamicEvidenceBuilder) —
//      SIEMPRE sanitizado, SIEMPRE versionable, nunca contiene material
//      crudo — sin cambios de política aquí.
//   B) este artefacto — SIEMPRE fuera del repositorio, puede contener
//      material crudo porque ESE es precisamente su propósito (preservar
//      lo que el Anexo exige), pero nunca se imprime ni se sube a Git.
//
// FAIL-CLOSED deliberado (XPAY-508 §6): la ejecución real de M4-T4 exige
// esta ruta configurada y válida ANTES de intentar ningún HTTP — nunca se
// gasta la única llamada real autorizada sin poder producir después la
// evidencia visual que el Anexo pide. Sección 6 del ticket permitía
// alternativamente sólo "advertir" — se elige el modo más estricto
// (bloquear), consistente con la disciplina de "una llamada, cero
// reintentos" ya aplicada en todo el harness: mejor no gastar la llamada
// que gastarla y quedar igualmente sin la evidencia requerida.
public static class M4T4PrivateCapture
{
    public const string EnvPrivateCaptureDir = "PASSPORT_TEST_M4_T4_PRIVATE_CAPTURE_DIR";

    public enum ValidationOutcome
    {
        Valid,
        Missing,
        InsideRepository,
        NotUsableAsPath,
    }

    // Función PURA — sólo manipulación de strings de ruta (Path.GetFullPath
    // es puramente lógico, no toca el disco). Nunca hace I/O. 100%
    // testeable sin filesystem real.
    //
    // "Dentro del repositorio" se determina por contención de path: la ruta
    // configurada, resuelta a absoluta, no puede ser igual ni estar anidada
    // bajo repositoryRootAbsolutePath. El llamador real (CreateQrDynamicExecutor)
    // usa Directory.GetCurrentDirectory() como raíz — el mismo criterio ya
    // usado implícitamente por EvidenceBaseDirectory en toda ejecución real
    // de este harness (siempre invocado desde la raíz del repositorio).
    public static ValidationOutcome Validate(string? configuredPath, string repositoryRootAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootAbsolutePath);

        if (string.IsNullOrWhiteSpace(configuredPath))
            return ValidationOutcome.Missing;

        string fullConfigured;
        string fullRepoRoot;
        try
        {
            fullConfigured = Path.GetFullPath(configuredPath);
            fullRepoRoot   = Path.GetFullPath(repositoryRootAbsolutePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ValidationOutcome.NotUsableAsPath;
        }

        // Normaliza el separador final para que la comparación de prefijo
        // nunca produzca un falso negativo/positivo por una barra sobrante.
        var repoRootWithSeparator = fullRepoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedConfigured = fullConfigured.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRepoRoot   = fullRepoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var isSameAsRepoRoot = string.Equals(normalizedConfigured, normalizedRepoRoot, StringComparison.Ordinal);
        var isNestedInRepoRoot = (fullConfigured + Path.DirectorySeparatorChar)
            .StartsWith(repoRootWithSeparator, StringComparison.Ordinal);

        if (isSameAsRepoRoot || isNestedInRepoRoot)
            return ValidationOutcome.InsideRepository;

        return ValidationOutcome.Valid;
    }

    // I/O — crea el directorio privado si falta y, cuando la plataforma lo
    // soporta (Unix), restringe sus permisos a sólo el propietario
    // (equivalente a chmod 700). Best-effort: nunca lanza por no poder
    // restringir permisos (p. ej. Windows) — sólo lanza si el directorio en
    // sí no puede crearse/usarse, para que el llamador lo trate como
    // LocalBlocked ANTES de cualquier HTTP.
    public static void EnsureDirectory(string validatedFullPath)
    {
        Directory.CreateDirectory(validatedFullPath);

        if (OperatingSystem.IsWindows())
            return; // Sin equivalente directo de chmod — no se fuerza nada.

        try
        {
            File.SetUnixFileMode(validatedFullPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            // Best-effort — la existencia/uso del directorio ya está
            // garantizada por CreateDirectory; no restringir permisos no es
            // motivo de bloqueo.
        }
    }

    // Escritura atómica — mismo patrón que EvidenceWriter (temporal + Move,
    // nunca sobrescribe silenciosamente un artefacto existente). Nombre:
    // {dir}/{caseId}-{executedAtUtc saneado}.json — nunca "evidence-...json"
    // para que sea inconfundible con el expediente versionable en una
    // inspección visual del directorio privado.
    public static string Write(string validatedFullDirectoryPath, string caseId, string executedAtUtc, object content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executedAtUtc);
        ArgumentNullException.ThrowIfNull(content);

        var fileName = $"{SanitizeForPath(caseId)}-{SanitizeForPath(executedAtUtc)}.private.json";
        var finalPath = Path.Combine(validatedFullDirectoryPath, fileName);

        if (File.Exists(finalPath))
            throw new InvalidOperationException(
                $"Ya existe un artefacto privado en '{finalPath}' — no se sobrescribe silenciosamente.");

        var json = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true });

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
