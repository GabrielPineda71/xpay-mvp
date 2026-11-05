using System.Text.Json;

namespace Xpay.PassportSandboxHarness;

// XPAY-531 — captura PRIVADA, NO versionable, del customer_id que Passport
// devuelva en una futura ejecución real de M5-T1-A (Create/Link Customer
// B). Mismo principio y estructura EXACTA que M4T4PrivateCapture (XPAY-508),
// M4T5PrivateCapture (XPAY-517) y M5PaymentPrivateCapture (XPAY-529):
// módulo PARALELO deliberadamente separado con su propia variable de
// entorno dedicada.
//
// FAIL-CLOSED (mismo criterio ya establecido): una ejecución real de
// Create Customer B exige esta ruta configurada y válida ANTES de
// intentar ningún HTTP.
public static class M5CustomerBPrivateCapture
{
    public const string EnvPrivateCaptureDir = "PASSPORT_TEST_M5_CUSTOMER_B_PRIVATE_CAPTURE_DIR";

    public enum ValidationOutcome
    {
        Valid,
        Missing,
        InsideRepository,
        NotUsableAsPath,
    }

    // Función PURA — idéntica en estructura a M4T4PrivateCapture.Validate y análogos.
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

    // Escritura atómica — mismo patrón que los módulos análogos.
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

    // XPAY-533 §9 — reserva de ejecución local, atómica, FUERA del repo
    // (vive en el mismo directorio de captura privada). Cierra la ventana de
    // carrera entre "no existe artefacto todavía" y "Passport ya fue
    // llamado": dos procesos del harness NUNCA pueden superar ambos el
    // chequeo de repetición y llamar a Passport al mismo tiempo, porque
    // sólo uno de los dos puede crear este archivo (FileMode.CreateNew es
    // atómico a nivel de sistema operativo — falla si el archivo ya existe).
    //
    // Nombre FIJO (no incluye timestamp): sólo puede existir UNA reserva
    // activa por caso a la vez, deliberadamente — a diferencia de los
    // artefactos ".private.json" (que sí incluyen timestamp para permitir
    // múltiples ejecuciones legítimas a lo largo del tiempo).
    //
    // XPAY-533 §10 — el lock se libera EXPLÍCITAMENTE sólo cuando el
    // executor sabe con certeza que ya no hay ambigüedad (antes de llamar a
    // Passport, o después de una respuesta de Passport ya capturada
    // localmente). Un lock que sobrevive (proceso muerto a mitad de un
    // intento real) es comportamiento DESEADO: fuerza
    // MANUAL_RECONCILIATION_REQUIRED en vez de permitir un reintento
    // automático silencioso que podría duplicar el recurso en Passport.
    public const string LockFileName = "m5-t1-a.execution.lock";

    public enum LockAcquisitionOutcome
    {
        Acquired,
        AlreadyHeld,
        Failed,
    }

    public static LockAcquisitionOutcome TryAcquireLock(string validatedFullDirectoryPath, out string lockFilePath)
    {
        lockFilePath = Path.Combine(validatedFullDirectoryPath, LockFileName);
        try
        {
            using var stream = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            // Contenido NO sensible — sólo un timestamp, útil para que un
            // operador humano diagnostique manualmente un lock huérfano.
            // NUNCA se escribe aquí ningún ID/dato de la fixture.
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            stream.Write(bytes, 0, bytes.Length);
            return LockAcquisitionOutcome.Acquired;
        }
        catch (IOException)
        {
            // Ya existe — otra ejecución concurrente lo sostiene, o quedó
            // huérfano tras un crash previo. Ambos casos exigen
            // MANUAL_RECONCILIATION_REQUIRED — nunca se borra
            // automáticamente aquí.
            return LockAcquisitionOutcome.AlreadyHeld;
        }
        catch (UnauthorizedAccessException)
        {
            return LockAcquisitionOutcome.Failed;
        }
    }

    // Best-effort DELIBERADO (mismo criterio que Write de artefactos): un
    // fallo al liberar NUNCA debe enmascarar el resultado real ya obtenido
    // de Passport. Un lock que no pudo borrarse queda como huérfano — misma
    // semántica de "exige reconciliación manual" que un crash.
    public static void ReleaseLock(string lockFilePath)
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
