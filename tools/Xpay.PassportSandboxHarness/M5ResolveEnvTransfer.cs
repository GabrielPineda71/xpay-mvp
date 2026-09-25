using System.Text.RegularExpressions;

namespace Xpay.PassportSandboxHarness;

// XPAY-545 §5 — mecanismo LOCAL, reutilizable y 100% testeable que
// transfiere el resolution_id crudo del artefacto privado de M5-T2-RESOLVE
// hacia PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID en un archivo de entorno
// (p. ej. ~/.passport-sandbox.env), SIN intervención manual y SIN imprimir
// el valor en ningún momento.
//
// DELIBERADAMENTE no está wireado a ningún comando del CLI del harness en
// esta fase — es una función pura de transferencia (parámetros = rutas de
// archivo), invocable manualmente por un operador o por un futuro comando
// dedicado. XPAY-545 prohíbe explícitamente ejecutarlo aquí con un valor
// real — sólo se prueba con datos fake (ver M5ResolveEnvTransferTests.cs).
//
// Tras una transferencia exitosa, escribe un marcador de CONSUMO
// ("M5-T2-RESOLVE-CONSUMED-{mismo sufijo}.private.json") en el MISMO
// directorio privado — nunca modifica el artefacto original (mismo
// principio de escritura atómica "crear, nunca sobrescribir" ya aplicado
// en todo el proyecto). Ese marcador es lo que permite a
// M5ResolveArtifactReader.IsBlockingForRepeat distinguir "vigente/no
// consumido" (bloquea) de "consumido y vencido" (permite una nueva
// ejecución real futura).
public static class M5ResolveEnvTransfer
{
    public enum TransferOutcome
    {
        Transferred,
        NoArtifactFound,
        ArtifactAmbiguous,
        ArtifactInvalid,
        EnvFileNotFound,
        EnvAlreadyHasResolutionId,
        Failed,
    }

    public const string EnvResolutionIdKey = "PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID";

    public static TransferOutcome TryTransfer(
        string privateCaptureDirFullPath,
        string envFilePath,
        string backupDirFullPath,
        DateTime nowUtc,
        out string? detail)
    {
        detail = null;

        if (!File.Exists(envFilePath))
        {
            detail = "Archivo de entorno no encontrado.";
            return TransferOutcome.EnvFileNotFound;
        }

        // Paso 1 — resolver EXACTAMENTE un artefacto válido de M5-T2-RESOLVE.
        var (outcome, artifact) = M5ResolveArtifactReader.Resolve(privateCaptureDirFullPath);
        switch (outcome)
        {
            case M5ResolveArtifactReader.ResolutionOutcome.DirectoryMissing:
            case M5ResolveArtifactReader.ResolutionOutcome.NoArtifactFound:
                detail = "No existe ningún artefacto privado M5-T2-RESOLVE.";
                return TransferOutcome.NoArtifactFound;

            case M5ResolveArtifactReader.ResolutionOutcome.MultipleArtifactsFound:
                detail = "Existe más de un artefacto privado M5-T2-RESOLVE — ambiguo, requiere revisión manual.";
                return TransferOutcome.ArtifactAmbiguous;

            case M5ResolveArtifactReader.ResolutionOutcome.Unreadable:
                detail = "El artefacto privado M5-T2-RESOLVE es ilegible/malformado.";
                return TransferOutcome.ArtifactInvalid;

            case M5ResolveArtifactReader.ResolutionOutcome.Found:
                break;
        }

        if (artifact is null || !M5ResolveArtifactReader.IsValid(artifact) || string.IsNullOrWhiteSpace(artifact.ResolutionId))
        {
            detail = "El artefacto privado M5-T2-RESOLVE no representa una resolución válida (case_id/resolution_id inesperados).";
            return TransferOutcome.ArtifactInvalid;
        }

        // Paso 2 — el env NUNCA debe tener ya un valor inesperado. Fail-closed:
        // nunca sobrescribir.
        var envContent = File.ReadAllText(envFilePath);
        if (Regex.IsMatch(envContent, $"^{Regex.Escape(EnvResolutionIdKey)}=", RegexOptions.Multiline))
        {
            detail = $"{EnvResolutionIdKey} ya existe en el archivo de entorno — no se sobrescribe.";
            return TransferOutcome.EnvAlreadyHasResolutionId;
        }

        try
        {
            // Paso 3 — backup privado ANTES de escribir.
            Directory.CreateDirectory(backupDirFullPath);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(backupDirFullPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
                {
                }
            }

            var backupFileName = $"env.bak.{nowUtc:yyyyMMddTHHmmss}";
            var backupPath = Path.Combine(backupDirFullPath, backupFileName);
            File.Copy(envFilePath, backupPath, overwrite: false);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(backupPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
                {
                }
            }

            // Paso 4 — escritura del env, sin imprimir el valor en ningún momento.
            var updated = envContent.EndsWith('\n') ? envContent : envContent + "\n";
            updated += $"{EnvResolutionIdKey}={artifact.ResolutionId}\n";
            File.WriteAllText(envFilePath, updated);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(envFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
                {
                }
            }

            // Paso 5 — marcador de consumo, NUNCA modifica el artefacto original.
            WriteConsumedMarker(privateCaptureDirFullPath, artifact, nowUtc);

            return TransferOutcome.Transferred;
        }
        catch (Exception ex)
        {
            detail = $"No se pudo completar la transferencia: {ex.GetType().Name}.";
            return TransferOutcome.Failed;
        }
    }

    private static void WriteConsumedMarker(string privateCaptureDirFullPath, M5ResolveArtifactReader.Artifact artifact, DateTime nowUtc)
    {
        var markerFileName = M5ResolveArtifactReader.ConsumedPrefix + artifact.SourceFileNameSuffix;
        var markerPath = Path.Combine(privateCaptureDirFullPath, markerFileName);

        if (File.Exists(markerPath))
            return; // ya consumido — no se sobrescribe, es idempotente por diseño.

        var content = new Dictionary<string, object?>
        {
            ["case_id"]                = M5ResolveArtifactReader.CaseId,
            ["consumed_utc"]           = nowUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            ["resolution_id_fingerprint"] = Fingerprint.Compute(artifact.ResolutionId),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(content, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var tempPath = markerPath + $".tmp-{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, json);
        try
        {
            File.Move(tempPath, markerPath);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }
}
