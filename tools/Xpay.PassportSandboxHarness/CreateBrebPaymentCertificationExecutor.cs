using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-529 — orquesta la ejecución REAL futura de la certificación M5
// Create Bre-B Payment (POST /v1/payments/breb). AISLAMIENTO
// ARQUITECTÓNICO OBLIGATORIO (XPAY-529 §2): este executor llama
// EXCLUSIVAMENTE a IPassportPaymentClient directamente — NUNCA a
// BrebPaymentService, PassportBrebRetiro, Wallet, ledger, ni
// BrebPaymentReconciliationService. Ningún parámetro de este método acepta
// ni construye ninguna dependencia de esos componentes —
// esto es estructuralmente imposible de violar por accidente, ya que la
// firma sólo recibe IConfiguration/IPassportPaymentClient/ICommitShaProvider,
// mismo patrón exacto que CreateQrDynamicExecutor (M4-T4) frente a
// CreateQrStaticExecutor.
//
// Config LEÍDA EXCLUSIVAMENTE AQUÍ (nunca en dry-run):
//   - PassportOptions.EnvOperationalAccountId ("PASSPORT_ACCOUNT_ID") —
//     MISMA variable de producto (BrebPaymentRequestBuilder la lee vía
//     BrebPaymentService), reutilizada porque sólo existe UNA cuenta
//     operativa real de XPAY en Passport — nunca una copia harness-only.
//     XPAY-529 NO instala esta variable — su ausencia bloquea localmente.
//   - PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID — dedicada, harness-only.
//     NUNCA una resolution_id histórica (XPAY-528 confirmó vigencia de
//     30 min — cualquier valor de una ejecución pasada estaría vencido).
//   - PASSPORT_TEST_M5_PAYMENT_RESOLUTION_EXPIRES_AT — OPCIONAL. Si está
//     presente y es parseable, se verifica vigencia localmente ANTES de
//     HTTP. Si está ausente, la vigencia NO puede verificarse localmente
//     — esto se reporta explícitamente (nunca se inventa un chequeo).
//   - PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE — dedicada. NO existe un valor
//     "oficial" confirmado de Passport para Payment (a diferencia del
//     amount de QR DYNAMIC, XPAY-512) — el operador debe decidirlo.
//   - PASSPORT_TEST_M5_PAYMENT_DISPLAY_NAME — OPCIONAL.
//   - M5PaymentPrivateCapture.EnvPrivateCaptureDir — FAIL-CLOSED antes de
//     HTTP, mismo criterio que M4T4PrivateCapture/M4T5PrivateCapture: sin
//     destino privado válido, nunca se gasta la única llamada real
//     autorizada.
//
// Currency SIEMPRE PassportPaymentClient.CopCurrency ("COP") — constante
// fija de protocolo, nunca configurable (mismo criterio ya usado en
// QrDynamicNegativeCaseFixture/CreateQrDynamicExecutor para el amount de QR).
public static class CreateBrebPaymentCertificationExecutor
{
    public const string EnvResolutionId          = "PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID";
    public const string EnvResolutionExpiresAtUtc = "PASSPORT_TEST_M5_PAYMENT_RESOLUTION_EXPIRES_AT";
    public const string EnvAmountValue           = "PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE";
    public const string EnvDisplayName           = "PASSPORT_TEST_M5_PAYMENT_DISPLAY_NAME";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportPaymentClient paymentClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? repositoryRootOverride = null,
        DateTime? nowUtcOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paymentClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        // Guard #5 — cuenta operativa.
        var accountId = configuration[PassportOptions.EnvOperationalAccountId];
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{PassportOptions.EnvOperationalAccountId} ausente al momento de intentar Create Payment (M5). " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #6 — resolution_id.
        var resolutionId = configuration[EnvResolutionId];
        if (string.IsNullOrWhiteSpace(resolutionId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{EnvResolutionId} ausente al momento de intentar Create Payment (M5). " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #7 — vigencia de la resolución, SÓLO cuando existe
        // metadata temporal verificable. Ausencia de esta metadata NO
        // bloquea por sí sola — es un gap reportado, no un chequeo inventado.
        var resolutionExpiresAtRaw = configuration[EnvResolutionExpiresAtUtc];
        var nowUtc = nowUtcOverride ?? DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(resolutionExpiresAtRaw))
        {
            if (!DateTime.TryParse(
                    resolutionExpiresAtRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var expiresAtUtc))
            {
                return new(KeyOperationOutcome.LocalBlocked, null,
                    $"{EnvResolutionExpiresAtUtc} presente pero no es una fecha ISO-8601 válida. " +
                    "No se realizó ninguna llamada a Passport.");
            }

            if (expiresAtUtc <= nowUtc)
            {
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "La resolución configurada para Create Payment (M5) está vencida " +
                    $"(verificado localmente vía {EnvResolutionExpiresAtUtc}). No se realizó ninguna llamada a Passport.");
            }
        }
        // XPAY-529 §16 — si EnvResolutionExpiresAtUtc está ausente, la
        // vigencia de la resolución NO puede verificarse localmente. Esto
        // se documenta como gap (ver evidencia/Notes de este caso) en vez
        // de bloquear artificialmente o de asumir que sigue vigente.

        // Guard #8 — amount (value siempre requerido; currency es
        // constante fija de protocolo, nunca leída del entorno).
        var amountValue = configuration[EnvAmountValue];
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{EnvAmountValue} ausente al momento de intentar Create Payment (M5). " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #9 — destino privado de captura, FAIL-CLOSED antes de HTTP
        // (mismo criterio XPAY-508/517).
        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();
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

        var displayName = configuration[EnvDisplayName]; // opcional — puede ser null.

        var request = new PassportCreatePaymentRequest(
            AccountId: accountId,
            ResolutionId: resolutionId,
            Amount: new PassportPaymentAmountRequest(Value: amountValue, Currency: PassportPaymentClient.CopCurrency))
        {
            DisplayName = displayName,
        };

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateBrebPaymentAsync — nunca
            // RetrievePaymentAsync ni ningún otro cliente Passport.
            var response = await paymentClient.CreateBrebPaymentAsync(request).ConfigureAwait(false);

            var privateArtifactCaptured = TryWritePrivateArtifact(
                privateCaptureDirFullPath, commitSha, executedAtUtc, accountId, response);

            var evidence = CreateBrebPaymentCertificationEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference,
                privateArtifactCaptured);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateBrebPaymentCertificationEvidenceBuilder.BuildPassportHttpFailure(
                request, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // XPAY-529 §10 — cadena privada Create → Retrieve: preserva
    // ÚNICAMENTE payment_id (crudo, fuera del repo) + fingerprints de
    // identidad — NUNCA account_id/resolution_id crudos. Best-effort
    // DELIBERADO (mismo criterio que XPAY-508): Passport ya respondió —
    // un fallo aquí nunca debe perder el registro sanitizado ya obtenido.
    private static bool TryWritePrivateArtifact(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string accountId, PassportPaymentResponse response)
    {
        try
        {
            var content = new Dictionary<string, object?>
            {
                ["case_id"]                = CreateBrebPaymentCertificationEvidenceBuilder.CaseId,
                ["captured_at_utc"]         = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"]      = commitSha,
                ["account_id_fingerprint"]  = Fingerprint.Compute(accountId),
                ["payment_id"]              = response.Id,
                ["status_at_creation"]      = response.Status,
            };

            M5PaymentPrivateCapture.Write(privateCaptureDirFullPath, "M5-PAYMENT-CREATE", ToTimestampForPath(executedAtUtc), content);
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
