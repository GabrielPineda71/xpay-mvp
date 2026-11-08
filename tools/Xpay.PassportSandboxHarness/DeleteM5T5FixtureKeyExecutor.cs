using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-613 — FASE B (PREP-DELETE) del fixture nuevo de M5-T5-E creado en la
// Fase A. Lee el key_id EXCLUSIVAMENTE desde el private capture CREATED de
// esa fase (M5T5DeletedFixtureArtifactReader) — NUNCA de env, NUNCA de un
// argumento CLI, NUNCA hardcodeado. Esto hace estructuralmente imposible
// eliminar por accidente la key activa de certificación, el fixture de
// M5-T5-D (que debe permanecer SUSPENDIDO, no eliminado, porque su
// evidencia final ya fue generada) o cualquier fixture histórico de M3/M4
// (ninguno aparece bajo este glob).
//
// Mirror ESTRUCTURAL de SuspendM5T5FixtureKeyExecutor (XPAY-576), con una
// diferencia de contrato: DELETE /v1/keys/{key_id} devuelve 204 No Content
// SIN body — IPassportKeyClient.DeleteKeyAsync devuelve Task, no
// Task<PassportKeyResponse>. No hay ningún status que leer ni registrar; la
// confirmación del proveedor ES el 204, que aquí se registra como
// delete_acknowledged=true.
//
// IRREVERSIBILIDAD: según la documentación de Passport (XPAY-292) esta
// operación es física/irreversible. Por eso el guard de identidad es
// estrictamente más fuerte que el de Suspend: además de resolver un único
// artifact CREATED válido, exige que su purpose sea el de M5-T5-E y
// compara el key_value en memoria contra la key activa de certificación
// antes de cualquier HTTP.
//
// AISLAMIENTO: llama EXCLUSIVAMENTE a IPassportKeyClient.DeleteKeyAsync
// (cliente productivo, SIN modificarlo) — nunca Create/Suspend/Activate/
// Resolve/ListKeys, y NUNCA conoce IPassportPaymentClient.
//
// REPEAT GUARD PERMANENTE propio de esta Fase B (independiente del guard
// de la Fase A): un marcador de archivo FIJO (nunca timestamped) — mismo
// mecanismo ya probado en CreatePaymentInvalidResolutionExecutor
// (XPAY-572) y SuspendM5T5FixtureKeyExecutor (XPAY-576) — cuya sola
// existencia impide un segundo Delete real, sin importar el resultado del
// primero. Se escribe INMEDIATAMENTE al recibir CUALQUIER respuesta de
// Passport (éxito o error) — ANTES de construir la evidencia pública.
public static class DeleteM5T5FixtureKeyExecutor
{
    public const string DeleteGuardCaseId = "M5-T5-E-DELETE-CONSUMED";
    private const string DeleteGuardTimestampToken = "MARKER";

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

        // Guard #1 — destino privado compartido, FAIL-CLOSED antes de HTTP.
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

        // Guard #2 — repeat guard PERMANENTE de esta Fase B: si el marcador
        // ya existe, Delete YA fue intentado alguna vez para este fixture —
        // nunca se repite, sin importar el resultado. Crítico dado que la
        // operación es irreversible: un segundo intento sólo podría
        // producir un 404 sin ningún valor de certificación.
        if (File.Exists(DeleteMarkerPath(privateCaptureDirFullPath)))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "LOCAL_BLOCKED_ALREADY_EXECUTED: M5-T5-E-DELETE ya fue intentado anteriormente " +
                "(marcador de consumo presente) — no se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — resolver EXACTAMENTE un artifact CREATED válido del
        // fixture de la Fase A.
        var (outcome, artifact) = M5T5DeletedFixtureArtifactReader.Resolve(privateCaptureDirFullPath);
        switch (outcome)
        {
            case M5T5DeletedFixtureArtifactReader.ResolutionOutcome.DirectoryMissing:
            case M5T5DeletedFixtureArtifactReader.ResolutionOutcome.NoArtifactFound:
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "No existe ningún artifact privado CREATED del fixture M5-T5-E (Fase A) — " +
                    "no se realizó ninguna llamada a Passport.");

            case M5T5DeletedFixtureArtifactReader.ResolutionOutcome.MultipleArtifactsFound:
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED_AMBIGUOUS: existe más de un artifact privado CREATED del fixture M5-T5-E — " +
                    "requiere revisión manual antes de continuar. No se realizó ninguna llamada a Passport.");

            case M5T5DeletedFixtureArtifactReader.ResolutionOutcome.Unreadable:
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "El artifact privado CREATED del fixture M5-T5-E es ilegible/malformado — " +
                    "no se realizó ninguna llamada a Passport.");

            case M5T5DeletedFixtureArtifactReader.ResolutionOutcome.Found:
                break;
        }

        if (artifact is null || !M5T5DeletedFixtureArtifactReader.IsValid(artifact))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El artifact privado CREATED del fixture M5-T5-E no representa un estado válido para Delete " +
                "(state/key_type/purpose/key_id/key_value inesperados). No se realizó ninguna llamada a Passport.");
        }

        // Guard #4 — PROTECCIÓN DE LLAVE: nunca la key activa de
        // certificación (comparación directa en memoria del key_value ya
        // leído del private capture). Dado que Delete es irreversible,
        // este guard es la última barrera antes de una pérdida permanente.
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
            // ÚNICAMENTE DeleteKeyAsync — nunca Create/Suspend/Activate/
            // Resolve/ListKeys. Devuelve Task (204 No Content, sin body):
            // completar sin excepción ES la confirmación del proveedor.
            await keyClient.DeleteKeyAsync(keyId).ConfigureAwait(false);

            TryMarkConsumed(privateCaptureDirFullPath, commitSha, executedAtUtc,
                keyIdFingerprint, keyValueFingerprint, httpStatus: null,
                safeErrorCode: null, safeErrorMessage: null,
                deleteAcknowledged: true, outcome: "EXPECTED_DELETE_SUCCESS");

            var evidence = DeleteM5T5FixtureKeyEvidenceBuilder.BuildSuccess(
                keyIdFingerprint, keyValueFingerprint, httpStatus: null,
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
                deleteAcknowledged: false, outcome: "DELETE_REJECTED");

            var evidence = DeleteM5T5FixtureKeyEvidenceBuilder.BuildPassportHttpFailure(
                keyIdFingerprint, keyValueFingerprint, ex.Message, httpStatus, commitSha, executedAtUtc,
                safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    private static string DeleteMarkerPath(string privateCaptureDirFullPath) =>
        Path.Combine(privateCaptureDirFullPath, $"{DeleteGuardCaseId}-{DeleteGuardTimestampToken}.private.json");

    // Best-effort DELIBERADO (mismo criterio que el resto del harness):
    // Passport ya respondió — un fallo aquí nunca debe disparar un
    // reintento de una operación irreversible. Registro FINGERPRINT-ONLY —
    // no duplica ningún valor crudo; el artifact CREATED de la Fase A sigue
    // siendo la única fuente de los valores reales, intacto (Delete elimina
    // la llave en Passport, nunca el registro privado local, que es lo que
    // la Fase C necesita para construir su Resolve).
    private static void TryMarkConsumed(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string keyIdFingerprint, string keyValueFingerprint, int? httpStatus,
        string? safeErrorCode, string? safeErrorMessage, bool deleteAcknowledged, string outcome)
    {
        try
        {
            var content = new Dictionary<string, object?>
            {
                ["case_id"]                = DeleteGuardCaseId,
                ["purpose"]                 = CreateM5T5DeletedKeyFixtureExecutor.FixturePurpose,
                ["phase"]                   = "DELETE",
                ["consumed_utc"]            = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"]      = commitSha,
                ["key_id_fingerprint"]      = keyIdFingerprint,
                ["key_value_fingerprint"]   = keyValueFingerprint,
                ["operation"]                = "delete",
                ["outcome"]                  = outcome,
                ["observed_http_status"]    = httpStatus,
                ["safe_error_code"]         = safeErrorCode,
                ["safe_error_message"]      = safeErrorMessage,
                // DELETE /v1/keys/{key_id} = 204 No Content, sin body: no
                // existe returned_status que registrar (a diferencia del
                // marcador de Suspend en M5-T5-D). La confirmación del
                // proveedor es el propio 204.
                ["delete_acknowledged"]     = deleteAcknowledged,
            };

            var path = M5PaymentPrivateCapture.Write(
                privateCaptureDirFullPath, DeleteGuardCaseId, DeleteGuardTimestampToken, content);

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
