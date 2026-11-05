using Microsoft.Extensions.Configuration;

namespace Xpay.PassportSandboxHarness;

// XPAY-546 — envoltorio CLI delgado sobre M5ResolveEnvTransfer (XPAY-545):
// expone de forma segura, vía el comando dedicado
// `configure-m5-payment-resolution`, la transferencia del resolution_id
// real desde el artefacto privado M5-T2-RESOLVE hacia
// PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID en ~/.passport-sandbox.env.
//
// NO llama a Passport — es una operación puramente LOCAL de archivos. Por
// eso este executor NO produce ningún EvidenceRecord ni escribe en
// docs/certificacion/passport-breb/ — a diferencia de todos los demás
// comandos del harness, que sí representan una interacción (real o
// abortada) con Passport. HarnessApp.RunAsync lo imprime directamente,
// sin pasar por EvidenceWriter.
//
// Única fuente autorizada del resolution_id: el artefacto privado
// M5-T2-RESOLVE (vía M5ResolveEnvTransfer -> M5ResolveArtifactReader).
// Esta firma NO acepta ningún resolution_id como argumento/parámetro —
// estructuralmente imposible de invocar con un valor "de fuera".
//
// Home del usuario resuelto vía Environment.SpecialFolder.UserProfile —
// NUNCA un home hardcodeado — mismo criterio "no hardcodear" ya aplicado
// en toda la integración.
public static class ConfigureM5PaymentResolutionExecutor
{
    public enum Outcome
    {
        Transferred,
        LocalBlocked,
    }

    public sealed record Result(Outcome Outcome, string? Detail);

    public static Result Execute(
        IConfiguration configuration,
        string repositoryRootAbsolutePath,
        string? envFilePathOverride = null,
        string? backupDirOverride = null,
        DateTime? nowUtcOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootAbsolutePath);

        // Guard #1 — mismo destino privado ya usado por Resolve/M5 Payment.
        var privateCaptureDirRaw = configuration[M5PaymentPrivateCapture.EnvPrivateCaptureDir];
        var captureValidation = M5PaymentPrivateCapture.Validate(privateCaptureDirRaw, repositoryRootAbsolutePath);
        if (captureValidation != M5PaymentPrivateCapture.ValidationOutcome.Valid)
        {
            return new(Outcome.LocalBlocked,
                $"Destino privado M5 Payment ({M5PaymentPrivateCapture.EnvPrivateCaptureDir}) inválido: {captureValidation}. " +
                "No se leyó ni modificó ningún archivo.");
        }

        string privateCaptureDirFullPath;
        try
        {
            privateCaptureDirFullPath = Path.GetFullPath(privateCaptureDirRaw!, repositoryRootAbsolutePath);
        }
        catch (Exception ex)
        {
            return new(Outcome.LocalBlocked, $"No se pudo resolver el destino privado: {ex.Message}");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var envFilePath  = envFilePathOverride  ?? Path.Combine(userProfile, ".passport-sandbox.env");
        var backupDir    = backupDirOverride    ?? Path.Combine(userProfile, ".xpay-private-backups");
        var nowUtc       = nowUtcOverride ?? DateTime.UtcNow;

        var transferOutcome = M5ResolveEnvTransfer.TryTransfer(
            privateCaptureDirFullPath, envFilePath, backupDir, nowUtc, out var detail);

        return transferOutcome == M5ResolveEnvTransfer.TransferOutcome.Transferred
            ? new(Outcome.Transferred, null)
            : new(Outcome.LocalBlocked, $"{transferOutcome}: {detail}");
    }
}
