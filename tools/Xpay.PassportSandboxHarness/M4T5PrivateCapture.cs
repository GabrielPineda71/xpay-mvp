using System.Text.Json;

namespace Xpay.PassportSandboxHarness;

// XPAY-517 — captura PRIVADA, NO versionable, de la respuesta real de
// Decode que Passport devuelva en la futura ejecución real de M4-T5. Mismo
// principio y estructura EXACTA que M4T4PrivateCapture (XPAY-508) — módulo
// PARALELO deliberadamente separado (nunca modifica M4T4PrivateCapture.cs
// ni ningún artefacto de M4-T4) con su propia variable de entorno
// dedicada, para no acoplar el ciclo de vida de ambos casos.
//
// A diferencia de M4T4PrivateCapture (que preserva campos individuales ya
// deserializados: qr_code_data/qr_code_image/qr_code_reference), este
// módulo se usa para preservar el JSON CRUDO completo de la respuesta de
// Decode (ver PassportQrClient.DecodeQrCodeWithRawCaptureAsync, XPAY-517)
// — necesario porque PassportDecodeQrCodeResponse no modela todos los
// campos que Passport podría devolver (tip, confirmado ausente; ver
// XPAY-516).
//
// Separación estricta de responsabilidades (mismo criterio que XPAY-508):
//   A) evidence.json (DecodeQrDynamicEvidenceBuilder) — SIEMPRE sanitizado,
//      SIEMPRE versionable, nunca contiene material crudo.
//   B) este artefacto — SIEMPRE fuera del repositorio, contiene el JSON
//      crudo de la respuesta (puede incluir key_value/qr_code_data/
//      merchant real), nunca se imprime ni se sube a Git.
//
// FAIL-CLOSED (mismo criterio que XPAY-508 §6): la ejecución real de
// M4-T5 exige esta ruta configurada y válida ANTES de intentar ningún
// HTTP.
public static class M4T5PrivateCapture
{
    public const string EnvPrivateCaptureDir = "PASSPORT_TEST_M4_T5_PRIVATE_CAPTURE_DIR";

    public enum ValidationOutcome
    {
        Valid,
        Missing,
        InsideRepository,
        NotUsableAsPath,
    }

    // Función PURA — idéntica en estructura a M4T4PrivateCapture.Validate.
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

    public static void EnsureDirectory(string validatedFullPath)
    {
        Directory.CreateDirectory(validatedFullPath);

        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(validatedFullPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
        }
    }

    // Escritura atómica — mismo patrón que M4T4PrivateCapture.Write.
    // Nombre: {dir}/{caseId}-{executedAtUtc saneado}.private.json.
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
