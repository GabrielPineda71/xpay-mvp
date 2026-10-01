using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-572 — orquesta la ÚNICA ejecución real autorizada de
// M5-T5-INVALID-RESOLUTION: un Create Bre-B Payment con un resolution_id
// SINTÉTICO (InvalidResolutionIdGenerator), nunca real, nunca proveniente
// de env/artifact/Passport. A diferencia de M5-T5-MISSING-AMOUNT/ACCOUNT
// (XPAY-570/571, bloqueo LOCAL garantizado), este subcaso SÍ alcanza
// Passport real — PassportPaymentClient.CreateBrebPaymentAsync sólo
// valida que resolution_id no esté vacío, nunca su validez semántica.
//
// AISLAMIENTO ARQUITECTÓNICO: llama EXCLUSIVAMENTE a
// IPassportPaymentClient.CreateBrebPaymentAsync (mismo cliente productivo
// que CreateBrebPaymentCertificationExecutor, XPAY-529) — nunca un segundo
// stack HTTP/OAuth. NUNCA invoca RetrievePaymentAsync — bajo NINGUNA
// circunstancia, ni siquiera si Passport acepta la request inesperadamente
// (XPAY-572 §12: UNEXPECTED_ACCEPTANCE nunca dispara un Retrieve).
//
// REPEAT GUARD PERMANENTE (XPAY-572 §6, distinto del guard time-boxed de
// M5ResolveArtifactReader): este caso se ejecuta A LO SUMO UNA VEZ EN LA
// VIDA del directorio privado — un marcador de archivo FIJO (nunca
// timestamped) reutiliza M5PaymentPrivateCapture.Write (que ya es
// "crear, nunca sobrescribir" por diseño) con un `executedAtUtc` literal
// fijo ("MARKER") en vez de un timestamp real, produciendo siempre el
// MISMO nombre de archivo — su sola existencia es el guard. El marcador se
// escribe INMEDIATAMENTE al recibir CUALQUIER respuesta de Passport
// (success o error) — ANTES de construir la evidencia pública — para que
// un fallo posterior al escribir evidence.json nunca habilite un reintento
// (XPAY-572 §6/§7: la recuperación es offline, vía este mismo marcador
// privado, nunca una segunda llamada).
public static class CreatePaymentInvalidResolutionExecutor
{
    public const string RepeatGuardCaseId = "M5-T5-INVALID-RESOLUTION-CONSUMED";
    private const string RepeatGuardTimestampToken = "MARKER";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportPaymentClient paymentClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? repositoryRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paymentClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();

        // Guard #1 — destino privado (compartido con M5 Payment/Resolve),
        // FAIL-CLOSED antes de HTTP.
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

        // Guard #2 — repeat guard PERMANENTE: si el marcador ya existe,
        // este caso YA fue ejecutado alguna vez — nunca se repite, sin
        // importar el resultado que tuvo. No se realiza ninguna llamada.
        if (IsAlreadyConsumed(privateCaptureDirFullPath))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "LOCAL_BLOCKED_ALREADY_EXECUTED: M5-T5-INVALID-RESOLUTION ya fue ejecutado anteriormente " +
                "(marcador de consumo presente) — no se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — cuenta operativa (misma variable de producto ya usada
        // por CreateBrebPaymentCertificationExecutor).
        var accountId = configuration[PassportOptions.EnvOperationalAccountId];
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{PassportOptions.EnvOperationalAccountId} ausente al momento de intentar Create Payment Invalid Resolution (M5-T5-INVALID-RESOLUTION).");
        }

        // Guard #4 — amount (misma variable ya usada por
        // CreateBrebPaymentCertificationExecutor — nunca un valor
        // hardcodeado distinto, para no divergir del amount ya autorizado
        // para certificación M5).
        var amountValue = configuration[CreateBrebPaymentCertificationExecutor.EnvAmountValue];
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{CreateBrebPaymentCertificationExecutor.EnvAmountValue} ausente al momento de intentar Create Payment Invalid Resolution (M5-T5-INVALID-RESOLUTION).");
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

        // ÚNICA fuente autorizada del resolution_id de este subcaso —
        // SIEMPRE sintético, nunca leído de configuración/artifact/Passport.
        var invalidResolutionId = InvalidResolutionIdGenerator.Generate();

        var request = new PassportCreatePaymentRequest(
            AccountId: accountId,
            ResolutionId: invalidResolutionId,
            Amount: new PassportPaymentAmountRequest(Value: amountValue, Currency: PassportPaymentClient.CopCurrency));

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateBrebPaymentAsync — nunca
            // RetrievePaymentAsync.
            var response = await paymentClient.CreateBrebPaymentAsync(request).ConfigureAwait(false);

            // Passport ACEPTÓ — resultado INESPERADO. Marcador de consumo
            // PRIMERO (best-effort, pero intentado antes que cualquier otra
            // cosa) para que ningún fallo posterior habilite un reintento.
            TryMarkConsumed(privateCaptureDirFullPath, commitSha, executedAtUtc,
                accountId, invalidResolutionId, httpStatus: null,
                safeErrorCode: null, safeErrorMessage: null,
                paymentId: response.Id, paymentStatus: response.Status, outcome: "UNEXPECTED_ACCEPTANCE");

            var evidence = CreatePaymentInvalidResolutionEvidenceBuilder.BuildUnexpectedAcceptance(
                accountId, invalidResolutionId, amountValue, response.Id, response.Status,
                commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, "UNEXPECTED_ACCEPTANCE — requiere revisión inmediata del director.");
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            // Passport RECHAZÓ — resultado ESPERADO de este subcaso
            // negativo. Marcador de consumo PRIMERO, mismo criterio.
            TryMarkConsumed(privateCaptureDirFullPath, commitSha, executedAtUtc,
                accountId, invalidResolutionId, httpStatus, safeErrorCode, safeErrorMessage,
                paymentId: null, paymentStatus: null, outcome: "EXPECTED_PROVIDER_REJECTION");

            var evidence = CreatePaymentInvalidResolutionEvidenceBuilder.BuildExpectedRejection(
                accountId, invalidResolutionId, amountValue, httpStatus, safeErrorCode, safeErrorMessage,
                commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    private static bool IsAlreadyConsumed(string privateCaptureDirFullPath)
    {
        var markerPath = Path.Combine(privateCaptureDirFullPath, MarkerFileName());
        return File.Exists(markerPath);
    }

    // Mismo nombre de archivo SIEMPRE — {caseId}-{token fijo}.private.json,
    // reutilizando M5PaymentPrivateCapture.Write (XPAY-529/545), que ya es
    // atómico y "crear, nunca sobrescribir" por diseño — su
    // InvalidOperationException al reintentar escribir es, en sí misma, la
    // prueba de que el guard es sólido (nunca se sobrescribe el marcador).
    private static string MarkerFileName() => $"{RepeatGuardCaseId}-{RepeatGuardTimestampToken}.private.json";

    // Private capture — best-effort DELIBERADO (mismo criterio que
    // TryWritePrivateArtifact en M5ResolveExecutor/
    // CreateBrebPaymentCertificationExecutor): Passport ya respondió (o no,
    // en el camino de excepción) — un fallo aquí nunca debe impedir que la
    // evidencia pública ya construida se reporte al llamador. Preserva
    // ÚNICAMENTE lo necesario para reconciliación manual futura — nunca el
    // resolution_id sintético en claro (ya es reproducible desde
    // InvalidResolutionIdGenerator, no necesita preservarse), nunca
    // account_id crudo.
    private static void TryMarkConsumed(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string accountId, string invalidResolutionId, int? httpStatus,
        string? safeErrorCode, string? safeErrorMessage,
        string? paymentId, string? paymentStatus, string outcome)
    {
        try
        {
            var content = new Dictionary<string, object?>
            {
                ["case_id"]                        = RepeatGuardCaseId,
                ["consumed_utc"]                    = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"]              = commitSha,
                ["account_id_fingerprint"]          = Fingerprint.Compute(accountId),
                ["invalid_resolution_fingerprint"]  = Fingerprint.Compute(invalidResolutionId),
                ["outcome"]                          = outcome,
                ["observed_http_status"]            = httpStatus,
                ["safe_error_code"]                 = safeErrorCode,
                ["safe_error_message"]              = safeErrorMessage,
                ["payment_id"]                       = paymentId, // sólo presente si Passport aceptó (UNEXPECTED_ACCEPTANCE)
                ["payment_status"]                   = paymentStatus,
            };

            M5PaymentPrivateCapture.Write(
                privateCaptureDirFullPath, RepeatGuardCaseId, RepeatGuardTimestampToken, content);
        }
        catch
        {
            // Best-effort: si el marcador ya existiera (no debería, dado
            // Guard #2) o la escritura fallara, nunca se reintenta la
            // llamada a Passport por esta razón — la respuesta ya fue
            // recibida y su evidencia pública se construye de todas formas.
        }
    }
}
