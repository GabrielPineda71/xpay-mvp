using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-613 — FASE C (RESOLVE-DELETED) y EJECUCIÓN FINAL del subcaso
// M5-T5-E (envío/resolución hacia clave Bre-B ELIMINADA). Lee
// key_type/key_value EXCLUSIVAMENTE desde el private capture CREATED de la
// Fase A (M5T5DeletedFixtureArtifactReader) — NUNCA de env, NUNCA de un
// argumento CLI, NUNCA hardcodeado — y exige, ANTES de HTTP, que exista
// una correlación Create->Delete válida con la Fase B (mismo key_id/
// key_value, y Delete reconocido por Passport con 204). Esto hace
// estructuralmente imposible resolver una key que nunca fue realmente
// eliminada, o una key cuya identidad no se verificó.
//
// POR QUÉ EL PRIVATE CAPTURE SOBREVIVE AL DELETE: Delete elimina la llave
// en Passport, nunca el registro privado local. Ese registro es la ÚNICA
// fuente del key_value que esta fase necesita — Resolve exige key_value,
// nunca key_id. Es exactamente lo que faltó en el fixture histórico de
// M4-T3-B (XPAY-492), cuyo key_value se perdió para siempre.
//
// AISLAMIENTO: llama EXCLUSIVAMENTE a IPassportKeyClient.ResolveKeyAsync
// (mismo cliente productivo que ResolveKeyExecutor/M5ResolveExecutor/
// ResolveM5T5SuspendedKeyExecutor, SIN modificarlo) — nunca Create/Suspend/
// Activate/Delete/ListKeys, y NUNCA IPassportPaymentClient: este caso
// termina en Resolve, jamás en Payment, bajo ninguna circunstancia — ni
// siquiera ante una resolución inesperada. La garantía es ESTRUCTURAL, no
// condicional: este tipo no recibe, no referencia y no puede alcanzar
// IPassportPaymentClient.
//
// RESULTADO ESPERADO: rechazo de Passport. Corroboración documental
// disponible: M4-T3-B obtuvo 404 resource_not_found ("key not found.")
// desde POST /v1/qrcodes contra una llave eliminada. Esta fase NO asume
// que resolve-key devolverá exactamente lo mismo — registra literalmente
// lo que el proveedor responda y clasifica la señal sin inventarla.
//
// REPEAT GUARD PERMANENTE propio de esta Fase C (independiente de los
// guards de las Fases A y B): mismo mecanismo de marcador fijo ya probado
// en CreatePaymentInvalidResolutionExecutor/SuspendM5T5FixtureKeyExecutor/
// ResolveM5T5SuspendedKeyExecutor.
public static class ResolveM5T5DeletedKeyExecutor
{
    public const string ResolveGuardCaseId = "M5-T5-E-RESOLVE-CONSUMED";
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

        // Guard #2 — repeat guard PERMANENTE de esta Fase C: si el marcador
        // ya existe, Resolve YA fue intentado para este fixture.
        if (File.Exists(ResolveMarkerPath(privateCaptureDirFullPath)))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "LOCAL_BLOCKED_ALREADY_EXECUTED: M5-T5-E-RESOLVE ya fue intentado anteriormente " +
                "(marcador de consumo presente) — no se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — resolver EXACTAMENTE un artifact CREATED válido de la
        // Fase A.
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
                    "no se realizó ninguna llamada a Passport.");

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
                "El artifact privado CREATED del fixture M5-T5-E no representa un estado válido — " +
                "no se realizó ninguna llamada a Passport.");
        }

        // Guard #4 — correlación Create -> Delete OBLIGATORIA: exige que la
        // Fase B haya eliminado EXACTAMENTE este mismo fixture (mismos
        // fingerprints) y que Passport lo haya reconocido (204). Nunca se
        // asume la eliminación sólo porque la Fase B devolvió result=PASS.
        var deleteMarker = M5T5DeletedFixtureArtifactReader.ResolveDeleteMarker(privateCaptureDirFullPath);
        if (!M5T5DeletedFixtureArtifactReader.IsDeleteCorrelationValid(artifact, deleteMarker))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "La correlación Create->Delete (Fase A -> Fase B) no es válida — el marcador de Delete está " +
                "ausente, no corresponde a este mismo fixture, o no confirmó la eliminación. " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #5 — customer_id (mismo contexto ya usado por M3-T2/
        // M5-T2-RESOLVE/M5-T5-D — nunca se crea un Customer nuevo).
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_CUSTOMER_ID ausente al momento de intentar Resolve M5-T5-E — " +
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

            // Passport aceptó — RESULTADO INESPERADO contra una llave
            // confirmadamente eliminada. Marcador de consumo PRIMERO
            // (fingerprint-only, nunca raw resolution_id — este caso nunca
            // lo usa y no hay ninguna razón para preservarlo).
            TryMarkConsumed(privateCaptureDirFullPath, commitSha, executedAtUtc,
                keyIdFingerprint, keyValueFingerprint, customerIdFingerprint,
                outcome: "UNEXPECTED_RESOLVE_SUCCESS", httpStatus: null,
                safeErrorCode: null, safeErrorMessage: null, resolutionIdFingerprint: Fingerprint.Compute(response.Id));

            var evidence = ResolveM5T5DeletedKeyEvidenceBuilder.BuildUnexpectedSuccess(
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

            var deletionSignalConfirmed = ClassifyDeletionSignal(httpStatus, safeErrorCode, safeErrorMessage);

            var evidence = ResolveM5T5DeletedKeyEvidenceBuilder.BuildExpectedRejection(
                keyIdFingerprint, keyValueFingerprint, httpStatus, safeErrorCode, safeErrorMessage,
                deletionSignalConfirmed, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // Determinación LITERAL, nunca inventada — mismo criterio que
    // ResolveM5T5SuspendedKeyExecutor.ClassifySuspensionSignal (XPAY-577):
    // sólo se afirma true si la respuesta sanitizada de Passport es
    // reconociblemente una señal de "recurso inexistente" (404, o un
    // código/mensaje que menciona not_found / not found). Cualquier otro
    // caso se clasifica como null (=UNKNOWN) en vez de asumir que la
    // eliminación fue la causa del rechazo. NUNCA devuelve false: la
    // ausencia de señal no es prueba de lo contrario.
    public static bool? ClassifyDeletionSignal(int? httpStatus, string? safeErrorCode, string? safeErrorMessage)
    {
        if (httpStatus == 404)
            return true;

        var combined = $"{safeErrorCode} {safeErrorMessage}";
        return combined.Contains("not_found", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? true
            : (bool?)null;
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
                ["purpose"]                     = CreateM5T5DeletedKeyFixtureExecutor.FixturePurpose,
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
