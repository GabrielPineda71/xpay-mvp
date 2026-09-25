using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-545 — orquesta la ejecución REAL futura de M5-T2-RESOLVE: un Resolve
// Key DEDICADO a la certificación de Payment B->A (M5-T2), separado por
// completo del caso M3-T2 (ya ACCEPTED/CONGELADO — nunca se toca
// ResolveKeyExecutor.cs/ResolveKeyEvidenceBuilder.cs). Reutiliza
// EXACTAMENTE el mismo cliente/contrato Passport ya existente
// (IPassportKeyClient.ResolveKeyAsync) y los MISMOS targets ya usados
// desde M3-T2 (PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_BREB_KEY_TYPE/
// PASSPORT_TEST_BREB_KEY — la misma KEY_A histórica) — nunca un segundo
// stack HTTP/OAuth, nunca una llave distinta.
//
// A diferencia de M3-T2, este executor SÍ preserva el resolution_id
// crudo — en un artefacto privado, fuera del repo, en el MISMO
// directorio ya configurado para M5 Payment
// (M5PaymentPrivateCapture.EnvPrivateCaptureDir, XPAY-545 §3) — porque
// M5-T2 necesita poder transferirlo programáticamente a
// PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID sin intervención manual
// (XPAY-544 identificó este gap: el M3-T2 histórico sólo guarda
// fingerprint, dejando el resolution_id irrecuperable).
//
// ORDEN DE GUARDS (documentado, mismo criterio XPAY-533): destino privado
// válido -> EnsureDirectory -> ADQUIRIR LOCK DE EJECUCIÓN -> NO-REPEAT
// GUARD (¿ya existe un artefacto M5-T2-RESOLVE vigente/no consumido?) ->
// recién entonces se leen los targets de KEY_A (customer_id/key_type/
// key_value) -> commit SHA -> ResolveKeyAsync. El no-repeat guard corre
// ANTES de tocar cualquier dato de identificación de KEY_A.
//
// Mismo criterio de liberación de lock que Customer B/Account B (XPAY-533
// §10): se libera en todo camino de salida anterior a la llamada Passport
// y tras una excepción capturada; tras éxito, sólo si el artefacto se
// escribió correctamente — si no, el lock queda huérfano a propósito
// (MANUAL_RECONCILIATION_REQUIRED).
public static class M5ResolveExecutor
{
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

        // Guard #1 — destino privado (compartido con M5 Payment), FAIL-CLOSED
        // antes de HTTP y antes de cualquier dato de identificación.
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
            M5PaymentPrivateCapture.EnsureDirectory(privateCaptureDirFullPath);
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo preparar el destino privado de captura M5 Payment: {ex.Message}");
        }

        // Guard #2 — reserva de ejecución dedicada a Resolve (XPAY-545 §6/§9,
        // mismo mecanismo XPAY-533).
        var lockOutcome = M5PaymentPrivateCapture.TryAcquireResolveLock(privateCaptureDirFullPath, out var lockFilePath);
        if (lockOutcome != M5PaymentPrivateCapture.LockAcquisitionOutcome.Acquired)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "No se pudo adquirir la reserva de ejecución de M5-T2 Resolve — " +
                "ya existe un lock activo (ejecución concurrente en curso, o pendiente de reconciliación manual " +
                "tras un intento previo interrumpido). No se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — no-repeat guard, basado en vigencia real (XPAY-545 §6).
        var (repeatOutcome, repeatArtifact) = M5ResolveArtifactReader.Resolve(privateCaptureDirFullPath);
        switch (repeatOutcome)
        {
            case M5ResolveArtifactReader.ResolutionOutcome.Found when repeatArtifact is not null && M5ResolveArtifactReader.IsValid(repeatArtifact)
                && M5ResolveArtifactReader.IsBlockingForRepeat(privateCaptureDirFullPath, repeatArtifact, executedAtUtc):
                M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED_ALREADY_EXECUTED: ya existe un resolution_id de M5-T2 vigente y/o no consumido " +
                    "todavía. No se realizó ninguna llamada a Passport.");

            case M5ResolveArtifactReader.ResolutionOutcome.Found when repeatArtifact is not null && M5ResolveArtifactReader.IsValid(repeatArtifact):
                // Válido pero ya consumido y vencido — se permite continuar (caso A efectivo).
                break;

            case M5ResolveArtifactReader.ResolutionOutcome.Found:
                M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED: existe un artefacto privado M5-T2-RESOLVE con formato o case_id inesperado " +
                    "— requiere revisión manual antes de continuar. No se realizó ninguna llamada a Passport.");

            case M5ResolveArtifactReader.ResolutionOutcome.MultipleArtifactsFound:
                M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED_AMBIGUOUS: se encontró más de un artefacto privado M5-T2-RESOLVE — " +
                    "requiere revisión manual antes de continuar. No se realizó ninguna llamada a Passport.");

            case M5ResolveArtifactReader.ResolutionOutcome.Unreadable:
                M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED: el directorio contiene un artefacto privado M5-T2-RESOLVE ilegible/malformado " +
                    "— requiere revisión manual antes de continuar. No se realizó ninguna llamada a Passport.");

            case M5ResolveArtifactReader.ResolutionOutcome.DirectoryMissing:
            case M5ResolveArtifactReader.ResolutionOutcome.NoArtifactFound:
                break;
        }

        // Guard #4 — targets de KEY_A/CUSTOMER_A, MISMOS ya usados desde M3-T2.
        var customerId  = configuration[HarnessTargetConfig.EnvCustomerId];
        var rawKeyType  = configuration[HarnessTargetConfig.EnvBrebKeyType];
        var keyValue    = configuration[HarnessTargetConfig.EnvBrebKeyValue];

        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(keyValue))
        {
            M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_BREB_KEY ausente(s) al momento de intentar M5-T2 Resolve.");
        }

        // Mapeo ESTRICTO — mismo criterio que ResolveKeyExecutor (M3-T2):
        // nunca se normaliza un sinónimo no confirmado por Passport.
        if (string.IsNullOrWhiteSpace(rawKeyType)
            || !Enum.TryParse<PassportKeyType>(rawKeyType, ignoreCase: false, out var keyType)
            || !Enum.IsDefined(keyType))
        {
            M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_BREB_KEY_TYPE no es un nombre válido de PassportKeyType (ID/PHONE/EMAIL/ALPHA/BCODE).");
        }

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        var request = new PassportResolveKeyRequest(
            CustomerId: customerId,
            Key: new PassportKeyRequest(keyType, keyValue));

        try
        {
            // Exactamente UNA llamada — sin reintentos. ÚNICAMENTE
            // ResolveKeyAsync — nunca Create/Suspend/Activate/Delete/ListKeys.
            var response = await keyClient.ResolveKeyAsync(request).ConfigureAwait(false);

            // XPAY-545 §10: el lock sólo se libera si el artefacto privado
            // se escribió con éxito (mismo criterio de ventana de crash,
            // XPAY-533 §10).
            var privateArtifactCaptured = TryWritePrivateArtifact(
                privateCaptureDirFullPath, commitSha, executedAtUtc, customerId, keyValue, response);

            if (privateArtifactCaptured)
                M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);

            var evidence = M5ResolveEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference,
                privateArtifactCaptured);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            M5PaymentPrivateCapture.ReleaseResolveLock(lockFilePath);

            var evidence = M5ResolveEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus: null, commitSha, executedAtUtc);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // Preserva EXCLUSIVAMENTE lo necesario para: (a) transferir el
    // resolution_id a env sin intervención manual, (b) evaluar vigencia
    // real en un futuro no-repeat guard. NUNCA account_id operativo, NUNCA
    // datos de owner/participant/account (PII). Best-effort DELIBERADO:
    // Passport ya respondió — un fallo aquí nunca debe perder el registro
    // sanitizado ya obtenido.
    private static bool TryWritePrivateArtifact(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string customerId, string keyValue, PassportResolveKeyResponse response)
    {
        try
        {
            var content = new Dictionary<string, object?>
            {
                ["case_id"]                  = M5ResolveArtifactReader.CaseId,
                ["captured_at_utc"]           = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"]        = commitSha,
                ["customer_id_fingerprint"]   = Fingerprint.Compute(customerId),
                ["key_value_fingerprint"]     = Fingerprint.Compute(keyValue),
                ["resolution_id"]             = response.Id,
                ["resolved_at_raw"]           = response.ResolvedAt,
                ["expires_at_raw"]            = response.ExpiresAt,
            };

            M5PaymentPrivateCapture.Write(
                privateCaptureDirFullPath, M5ResolveArtifactReader.CaseId, ToTimestampForPath(executedAtUtc), content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToTimestampForPath(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
