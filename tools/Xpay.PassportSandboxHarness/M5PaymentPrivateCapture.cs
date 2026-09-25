using System.Text.Json;

namespace Xpay.PassportSandboxHarness;

// XPAY-529 — captura PRIVADA, NO versionable, del payment_id que Passport
// devuelva en una futura ejecución real de Create Payment M5 — mismo
// principio y estructura EXACTA que M4T4PrivateCapture (XPAY-508) y
// M4T5PrivateCapture (XPAY-517): módulo PARALELO deliberadamente separado
// (nunca modifica los otros dos) con su propia variable de entorno
// dedicada, para no acoplar el ciclo de vida de M5 con M4.
//
// A diferencia de M4T4PrivateCapture, aquí lo crítico a preservar es
// payment_id (necesario para encadenar un futuro Retrieve, XPAY-529 §10)
// — nunca account_id/resolution_id crudos (esos permanecen exclusivamente
// en memoria durante la ejecución, nunca escritos a disco ni siquiera
// privadamente, salvo su fingerprint para verificación de identidad).
//
// FAIL-CLOSED (mismo criterio que XPAY-508/517): una ejecución real de
// Create Payment exige esta ruta configurada y válida ANTES de intentar
// ningún HTTP.
public static class M5PaymentPrivateCapture
{
    public const string EnvPrivateCaptureDir = "PASSPORT_TEST_M5_PAYMENT_PRIVATE_CAPTURE_DIR";

    public enum ValidationOutcome
    {
        Valid,
        Missing,
        InsideRepository,
        NotUsableAsPath,
    }

    // Función PURA — idéntica en estructura a M4T4PrivateCapture.Validate/M4T5PrivateCapture.Validate.
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

    // Escritura atómica — mismo patrón que M4T4PrivateCapture.Write/M4T5PrivateCapture.Write.
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

    // XPAY-545 — reserva de ejecución para M5-T2 Resolve (mismo mecanismo
    // ya probado en M5CustomerBPrivateCapture/M5AccountBPrivateCapture,
    // XPAY-533 — ver esa clase para el razonamiento completo). Nombre de
    // lock DEDICADO a Resolve ("m5-t2-resolve.execution.lock") — nunca
    // colisiona con un futuro lock de Create/Retrieve Payment sobre este
    // MISMO directorio compartido (XPAY-545 §3 decidió reutilizar este
    // directorio en vez de crear una variable de entorno nueva).
    public const string ResolveLockFileName = "m5-t2-resolve.execution.lock";

    public enum LockAcquisitionOutcome
    {
        Acquired,
        AlreadyHeld,
        Failed,
    }

    public static LockAcquisitionOutcome TryAcquireResolveLock(string validatedFullDirectoryPath, out string lockFilePath)
    {
        lockFilePath = Path.Combine(validatedFullDirectoryPath, ResolveLockFileName);
        try
        {
            using var stream = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            stream.Write(bytes, 0, bytes.Length);
            return LockAcquisitionOutcome.Acquired;
        }
        catch (IOException)
        {
            return LockAcquisitionOutcome.AlreadyHeld;
        }
        catch (UnauthorizedAccessException)
        {
            return LockAcquisitionOutcome.Failed;
        }
    }

    public static void ReleaseResolveLock(string lockFilePath)
    {
        try
        {
            if (File.Exists(lockFilePath))
                File.Delete(lockFilePath);
        }
        catch
        {
        }
    }
}
