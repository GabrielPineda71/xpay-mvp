using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-576 — FASE 2 (Suspend Key) del fixture nuevo de M5-T5-D creado en
// XPAY-575. Lee el key_id EXCLUSIVAMENTE desde el private capture CREATED
// de XPAY-575 (M5T5SuspendedFixtureArtifactReader) — NUNCA de env, NUNCA
// de un argumento CLI, NUNCA hardcodeado. Esto hace estructuralmente
// imposible repetir el error de XPAY-574 (usar un recurso cuya identidad
// nunca se verificó) o de suspender por accidente el fixture HISTÓRICO de
// M4-T3-A (que nunca aparece bajo este glob).
//
// AISLAMIENTO: llama EXCLUSIVAMENTE a IPassportKeyClient.SuspendKeyAsync
// (mismo cliente productivo que SuspendKeyExecutor, M3-T3, SIN
// modificarlo) — nunca Create/Activate/Delete/Resolve/ListKeys.
//
// REPEAT GUARD PERMANENTE propio de esta Fase 2 (independiente del guard
// de Create Key de XPAY-575): un marcador de archivo FIJO (nunca
// timestamped) — mismo mecanismo ya probado en
// CreatePaymentInvalidResolutionExecutor (XPAY-572) — cuya sola existencia
// impide una segunda Suspend real, sin importar el resultado de la
// primera. Se escribe INMEDIATAMENTE al recibir CUALQUIER respuesta de
// Passport (éxito o error) — ANTES de construir la evidencia pública.
public static class SuspendM5T5FixtureKeyExecutor
{
    public const string SuspendGuardCaseId = "M5-T5-D-SUSPEND-CONSUMED";
    private const string SuspendGuardTimestampToken = "MARKER";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportKeyClient keyClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? repositoryRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keyClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();

        // Guard #1 — destino privado (compartido con M5 Payment/Resolve/
        // fixture de XPAY-575), FAIL-CLOSED antes de HTTP.
        var privateCaptureDirRaw = configuration[M5PaymentPrivateCapture.EnvPrivateCaptureDir];
        var captureValidation = M5PaymentPrivateCapture.Validate(privateCaptureDirRaw, repositoryRoot);
        if (captureValidation != M5PaymentPrivateCapture.ValidationOutcome.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M5 Payment ({M5PaymentPrivateCapture.EnvPrivateCaptureDir}) inválido: {captureValidation}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        string privateCaptureDirFullPath;
        try
        {
            privateCaptureDirFullPath = Path.GetFullPath(privateCaptureDirRaw!, repositoryRoot);
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el destino privado de captura M5 Payment: {ex.Message}");
        }

        // Guard #2 — repeat guard PERMANENTE de esta Fase 2: si el
        // marcador ya existe, Suspend YA fue intentado alguna vez para
        // este fixture — nunca se repite, sin importar el resultado.
        if (IsAlreadyConsumed(privateCaptureDirFullPath))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "LOCAL_BLOCKED_ALREADY_EXECUTED: M5-T5-D-SUSPEND ya fue intentado anteriormente " +
                "(marcador de consumo presente) — no se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — resolver EXACTAMENTE un artifact CREATED válido del
        // fixture de XPAY-575.
        var (outcome, artifact) = M5T5SuspendedFixtureArtifactReader.Resolve(privateCaptureDirFullPath);
        switch (outcome)
        {
            case M5T5SuspendedFixtureArtifactReader.ResolutionOutcome.DirectoryMissing:
            case M5T5SuspendedFixtureArtifactReader.ResolutionOutcome.NoArtifactFound:
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "No existe ningún artifact privado CREATED del fixture M5-T5-D (XPAY-575) — " +
                    "no se realizó ninguna llamada a Passport.");

            case M5T5SuspendedFixtureArtifactReader.ResolutionOutcome.MultipleArtifactsFound:
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED_AMBIGUOUS: existe más de un artifact privado CREATED del fixture M5-T5-D — " +
                    "requiere revisión manual antes de continuar. No se realizó ninguna llamada a Passport.");

            case M5T5SuspendedFixtureArtifactReader.ResolutionOutcome.Unreadable:
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "El artifact privado CREATED del fixture M5-T5-D es ilegible/malformado — " +
                    "no se realizó ninguna llamada a Passport.");

            case M5T5SuspendedFixtureArtifactReader.ResolutionOutcome.Found:
                break;
        }

        if (artifact is null || !M5T5SuspendedFixtureArtifactReader.IsValid(artifact))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El artifact privado CREATED del fixture M5-T5-D no representa un estado válido para Suspend " +
                "(state/key_type/key_id/key_value inesperados). No se realizó ninguna llamada a Passport.");
        }

        // Guard #4 — nunca la key activa de certificación (comparación
        // directa en memoria del key_value ya leído del private capture).
        var activeKeyValue = configuration[HarnessTargetConfig.EnvBrebKeyValue];
        if (!string.IsNullOrEmpty(activeKeyValue) && string.Equals(artifact.KeyValue, activeKeyValue, StringComparison.Ordinal))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El fixture privado coincide con la key activa de certificación — abortado por seguridad, " +
                "sin ninguna llamada a Passport.");
        }

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        var keyId = artifact.KeyId!; // ya validado no-vacío por IsValid.
        var keyIdFingerprint = artifact.KeyIdFingerprint ?? Fingerprint.Compute(keyId);
        var keyValueFingerprint = artifact.KeyValueFingerprint ?? Fingerprint.Compute(artifact.KeyValue);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP.
            // ÚNICAMENTE SuspendKeyAsync — nunca Create/Activate/Delete/
            // Resolve/ListKeys.
            var response = await keyClient.SuspendKeyAsync(keyId).ConfigureAwait(false);

            TryMarkConsumed(privateCaptureDirFullPath, commitSha, executedAtUtc,
                keyIdFingerprint, keyValueFingerprint, httpStatus: null,
                safeErrorCode: null, safeErrorMessage: null, returnedStatus: response.Status, outcome: "EXPECTED_SUSPEND_SUCCESS");

            var evidence = SuspendM5T5FixtureKeyEvidenceBuilder.BuildSuccess(
                keyIdFingerprint, keyValueFingerprint, response, httpStatus: null,
                commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            TryMarkConsumed(privateCaptureDirFullPath, commitSha, executedAtUtc,
                keyIdFingerprint, keyValueFingerprint, httpStatus, safeErrorCode, safeErrorMessage,
                returnedStatus: null, outcome: "SUSPEND_REJECTED");

            var evidence = SuspendM5T5FixtureKeyEvidenceBuilder.BuildPassportHttpFailure(
                keyIdFingerprint, keyValueFingerprint, ex.Message, httpStatus, commitSha, executedAtUtc,
                safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    private static bool IsAlreadyConsumed(string privateCaptureDirFullPath)
    {
        var markerPath = Path.Combine(privateCaptureDirFullPath, MarkerFileName());
        return File.Exists(markerPath);
    }

    private static string MarkerFileName() => $"{SuspendGuardCaseId}-{SuspendGuardTimestampToken}.private.json";

    // Best-effort DELIBERADO (mismo criterio que el resto del harness):
    // Passport ya respondió — un fallo aquí nunca debe disparar un
    // reintento. Registro FINGERPRINT-ONLY — no duplica ningún valor
    // crudo, el artifact CREATED de XPAY-575 sigue siendo la única fuente
    // de los valores reales, intacto.
    private static void TryMarkConsumed(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string keyIdFingerprint, string keyValueFingerprint, int? httpStatus,
        string? safeErrorCode, string? safeErrorMessage, string? returnedStatus, string outcome)
    {
        try
        {
            var content = new Dictionary<string, object?>
            {
                ["case_id"]                = SuspendGuardCaseId,
                ["purpose"]                 = "M5-T5-D-SUSPENDED-KEY-FIXTURE",
                ["phase"]                   = "SUSPEND",
                ["consumed_utc"]            = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"]      = commitSha,
                ["key_id_fingerprint"]      = keyIdFingerprint,
                ["key_value_fingerprint"]   = keyValueFingerprint,
                ["operation"]                = "suspend",
                ["outcome"]                  = outcome,
                ["observed_http_status"]    = httpStatus,
                ["safe_error_code"]         = safeErrorCode,
                ["safe_error_message"]      = safeErrorMessage,
                ["returned_status"]         = returnedStatus,
            };

            var path = M5PaymentPrivateCapture.Write(
                privateCaptureDirFullPath, SuspendGuardCaseId, SuspendGuardTimestampToken, content);

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch
        {
            // Best-effort — la respuesta ya fue recibida y su evidencia
            // pública se construye de todas formas.
        }
    }
}
