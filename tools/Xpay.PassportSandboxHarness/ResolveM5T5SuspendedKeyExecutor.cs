using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-577 — FASE 3 (Resolve real) y EJECUCIÓN FINAL del subcaso
// M5-T5-D (envío/resolución hacia clave Bre-B suspendida). Lee
// key_type/key_value EXCLUSIVAMENTE desde el private capture CREATED de
// XPAY-575 (M5T5SuspendedFixtureArtifactReader) — NUNCA de env, NUNCA de
// un argumento CLI, NUNCA hardcodeado — y exige, ANTES de HTTP, que exista
// una correlación Create→Suspend válida con XPAY-576 (mismo key_id/
// key_value, y Suspend confirmado con status "SUSPENDED"). Esto hace
// estructuralmente imposible repetir el error de identidad de XPAY-573/574
// o resolver accidentalmente una key que nunca fue realmente suspendida.
//
// AISLAMIENTO: llama EXCLUSIVAMENTE a IPassportKeyClient.ResolveKeyAsync
// (mismo cliente productivo que ResolveKeyExecutor/M5ResolveExecutor, SIN
// modificarlo) — nunca Create/Suspend/Activate/Delete/ListKeys, y NUNCA
// IPassportPaymentClient (este caso termina en Resolve, jamás en Payment,
// bajo ninguna circunstancia — ni siquiera ante una resolución inesperada).
//
// REPEAT GUARD PERMANENTE propio de esta Fase 3 (independiente de los
// guards de XPAY-575/576): mismo mecanismo de marcador fijo ya probado en
// CreatePaymentInvalidResolutionExecutor/SuspendM5T5FixtureKeyExecutor.
public static class ResolveM5T5SuspendedKeyExecutor
{
    public const string ResolveGuardCaseId = "M5-T5-D-RESOLVE-CONSUMED";
    private const string ResolveGuardTimestampToken = "MARKER";

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

        // Guard #2 — repeat guard PERMANENTE de esta Fase 3: si el
        // marcador ya existe, Resolve YA fue intentado para este fixture.
        if (File.Exists(ResolveMarkerPath(privateCaptureDirFullPath)))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "LOCAL_BLOCKED_ALREADY_EXECUTED: M5-T5-D-RESOLVE ya fue intentado anteriormente " +
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
                    "no se realizó ninguna llamada a Passport.");

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
                "El artifact privado CREATED del fixture M5-T5-D no representa un estado válido — " +
                "no se realizó ninguna llamada a Passport.");
        }

        // Guard #4 — correlación Create -> Suspend OBLIGATORIA: exige que
        // XPAY-576 haya suspendido EXACTAMENTE este mismo fixture (mismos
        // fingerprints) y que Passport haya devuelto explícitamente
        // "SUSPENDED" — nunca se asume la suspensión sólo porque XPAY-576
        // devolvió result=PASS.
        var suspendMarker = M5T5SuspendedFixtureArtifactReader.ResolveSuspendMarker(privateCaptureDirFullPath);
        if (!M5T5SuspendedFixtureArtifactReader.IsSuspendCorrelationValid(artifact, suspendMarker))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "La correlación Create->Suspend (XPAY-575->XPAY-576) no es válida — el marcador de Suspend " +
                "está ausente, no corresponde a este mismo fixture, o no confirmó estado SUSPENDED. " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #5 — customer_id (mismo contexto ya usado por M3-T2/
        // M5-T2-RESOLVE — nunca se crea un Customer nuevo).
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_CUSTOMER_ID ausente al momento de intentar Resolve M5-T5-D — " +
                "no se realizó ninguna llamada a Passport.");
        }

        // Guard #6 — nunca la key activa de certificación (defensa en
        // profundidad, comparación directa en memoria).
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

        var keyIdFingerprint = artifact.KeyIdFingerprint ?? Fingerprint.Compute(artifact.KeyId);
        var keyValueFingerprint = artifact.KeyValueFingerprint ?? Fingerprint.Compute(artifact.KeyValue);
        var customerIdFingerprint = Fingerprint.Compute(customerId);

        var request = new PassportResolveKeyRequest(
            CustomerId: customerId,
            Key: new PassportKeyRequest(PassportKeyType.BCODE, artifact.KeyValue!));

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP.
            // ÚNICAMENTE ResolveKeyAsync — nunca ningún otro método, nunca
            // IPassportPaymentClient.
            var response = await keyClient.ResolveKeyAsync(request).ConfigureAwait(false);

            // Passport aceptó — RESULTADO INESPERADO. Marcador de consumo
            // PRIMERO (fingerprint-only, nunca raw resolution_id — no hay
            // ninguna razón para preservarlo, este caso nunca lo usa).
            TryMarkConsumed(privateCaptureDirFullPath, commitSha, executedAtUtc,
                keyIdFingerprint, keyValueFingerprint, customerIdFingerprint,
                outcome: "UNEXPECTED_RESOLVE_SUCCESS", httpStatus: null,
                safeErrorCode: null, safeErrorMessage: null, resolutionIdFingerprint: Fingerprint.Compute(response.Id));

            var evidence = ResolveM5T5SuspendedKeyEvidenceBuilder.BuildUnexpectedSuccess(
                keyIdFingerprint, keyValueFingerprint, response, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence,
                "UNEXPECTED_RESOLVE_SUCCESS — requiere revisión inmediata del director. NO se creó ningún Payment.");
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            // Passport rechazó — resultado ESPERADO de este subcaso
            // negativo. Marcador de consumo PRIMERO, mismo criterio.
            TryMarkConsumed(privateCaptureDirFullPath, commitSha, executedAtUtc,
                keyIdFingerprint, keyValueFingerprint, customerIdFingerprint,
                outcome: "EXPECTED_PROVIDER_REJECTION", httpStatus, safeErrorCode, safeErrorMessage,
                resolutionIdFingerprint: null);

            var currentSuspensionConfirmed = ClassifySuspensionSignal(safeErrorCode, safeErrorMessage);

            var evidence = ResolveM5T5SuspendedKeyEvidenceBuilder.BuildExpectedRejection(
                keyIdFingerprint, keyValueFingerprint, httpStatus, safeErrorCode, safeErrorMessage,
                currentSuspensionConfirmed, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // XPAY-577 §12 — determinación LITERAL, nunca inventada: sólo se
    // afirma CURRENT_SUSPENSION_CONFIRMED=true si el código/mensaje
    // sanitizado de Passport menciona explícitamente "suspend" — cualquier
    // otro caso (not_found genérico, invalid, etc.) se clasifica como
    // "unknown" en vez de asumir que la suspensión fue la causa.
    public static bool? ClassifySuspensionSignal(string? safeErrorCode, string? safeErrorMessage)
    {
        var combined = $"{safeErrorCode} {safeErrorMessage}";
        return combined.Contains("suspend", StringComparison.OrdinalIgnoreCase) ? true : (bool?)null;
    }

    private static string ResolveMarkerPath(string privateCaptureDirFullPath) =>
        Path.Combine(privateCaptureDirFullPath, $"{ResolveGuardCaseId}-{ResolveGuardTimestampToken}.private.json");

    // Best-effort DELIBERADO — Passport ya respondió; un fallo aquí nunca
    // dispara un reintento. Registro FINGERPRINT-ONLY.
    private static void TryMarkConsumed(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string keyIdFingerprint, string keyValueFingerprint, string customerIdFingerprint,
        string outcome, int? httpStatus, string? safeErrorCode, string? safeErrorMessage,
        string? resolutionIdFingerprint)
    {
        try
        {
            var content = new Dictionary<string, object?>
            {
                ["case_id"]                    = ResolveGuardCaseId,
                ["purpose"]                     = "M5-T5-D-SUSPENDED-KEY-FIXTURE",
                ["phase"]                        = "RESOLVE",
                ["consumed_utc"]                = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"]          = commitSha,
                ["key_id_fingerprint"]          = keyIdFingerprint,
                ["key_value_fingerprint"]       = keyValueFingerprint,
                ["customer_id_fingerprint"]     = customerIdFingerprint,
                ["operation"]                    = "resolve",
                ["outcome"]                      = outcome,
                ["observed_http_status"]        = httpStatus,
                ["safe_error_code"]             = safeErrorCode,
                ["safe_error_message"]          = safeErrorMessage,
                ["resolution_id_fingerprint"]   = resolutionIdFingerprint,
            };

            var path = M5PaymentPrivateCapture.Write(
                privateCaptureDirFullPath, ResolveGuardCaseId, ResolveGuardTimestampToken, content);

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
        }
    }
}
