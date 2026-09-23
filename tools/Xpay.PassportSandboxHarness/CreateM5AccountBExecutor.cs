using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-531 — orquesta la ejecución REAL futura de la certificación M5-T1-B
// (Create/Link Account B, POST /v1/accounts/link). AISLAMIENTO
// ARQUITECTÓNICO OBLIGATORIO: este executor llama EXCLUSIVAMENTE a
// IPassportCustomerAccountClient.LinkAccountAsync directamente — nunca a
// ningún servicio de producto XPAY.
//
// XPAY-531 §10 — customer_id debe obtenerse EXCLUSIVAMENTE del artefacto
// privado de M5-T1-A (M5CustomerBArtifactReader). Está PROHIBIDO: un
// customer_id hardcodeado, el Customer A, ingreso manual vía argumento CLI,
// o un customer histórico de cualquier otra fase.
//
// Config LEÍDA EXCLUSIVAMENTE AQUÍ (nunca en dry-run):
//   - M5AccountBPrivateCapture.EnvPrivateCaptureDir — FAIL-CLOSED antes de
//     HTTP, leído PRIMERO (XPAY-533): es una ruta de configuración, no PII,
//     y se necesita para el no-repeat guard antes de tocar account_number o
//     el artefacto de Customer B.
//   - PASSPORT_TEST_M5_ACCOUNT_B_ACCOUNT_NUMBER — dedicada, harness-only.
//     XPAY-531 §11 prohíbe explícitamente usar PASSPORT_ACCOUNT_ID (la
//     cuenta operativa de XPAY) como account_number de Account B.
//   - M5CustomerBPrivateCapture.EnvPrivateCaptureDir — READ-ONLY aquí (el
//     mismo directorio que M5-T1-A ya usó para escribir). Ausente/insuficiente
//     bloquea localmente.
//
// XPAY-533 — ORDEN DE GUARDS (documentado explícitamente, ver §6 del
// ticket): destino privado PROPIO válido -> EnsureDirectory -> ADQUIRIR
// LOCK DE EJECUCIÓN -> NO-REPEAT GUARD (¿ya existe un artefacto M5-T1-B
// válido?) -> account_number presente -> destino privado de Customer B
// legible -> LEER artefacto de Customer B (customer_id) -> commit SHA ->
// LinkAccountAsync. El no-repeat guard corre ANTES de tocar account_number
// o el artefacto de Customer B — si el caso ya fue ejecutado, se bloquea
// sin procesar ningún dato sensible adicional.
//
// Mismo criterio de liberación de lock que CreateM5CustomerBExecutor: se
// libera en todo camino de salida anterior a la llamada Passport y tras una
// excepción capturada; tras éxito, sólo si el artefacto se escribió
// correctamente — de lo contrario queda huérfano deliberadamente
// (MANUAL_RECONCILIATION_REQUIRED, XPAY-533 §10).
public static class CreateM5AccountBExecutor
{
    public const string EnvAccountNumber = "PASSPORT_TEST_M5_ACCOUNT_B_ACCOUNT_NUMBER";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportCustomerAccountClient customerAccountClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? repositoryRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(customerAccountClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();

        // Guard #1 — destino privado PROPIO de captura de Account B,
        // FAIL-CLOSED antes de HTTP y antes de tocar cualquier dato
        // sensible (mismo criterio que los demás módulos de captura privada).
        var accountCaptureDirRaw = configuration[M5AccountBPrivateCapture.EnvPrivateCaptureDir];
        var accountCaptureValidation = M5AccountBPrivateCapture.Validate(accountCaptureDirRaw, repositoryRoot);
        if (accountCaptureValidation != M5AccountBPrivateCapture.ValidationOutcome.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M5 Account B ({M5AccountBPrivateCapture.EnvPrivateCaptureDir}) inválido: {accountCaptureValidation}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        string accountCaptureDirFullPath;
        try
        {
            accountCaptureDirFullPath = Path.GetFullPath(accountCaptureDirRaw!, repositoryRoot);
            M5AccountBPrivateCapture.EnsureDirectory(accountCaptureDirFullPath);
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo preparar el destino privado de captura M5 Account B: {ex.Message}");
        }

        // Guard #2 — reserva de ejecución (XPAY-533 §9).
        var lockOutcome = M5AccountBPrivateCapture.TryAcquireLock(accountCaptureDirFullPath, out var lockFilePath);
        if (lockOutcome != M5AccountBPrivateCapture.LockAcquisitionOutcome.Acquired)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "No se pudo adquirir la reserva de ejecución de Account B (M5-T1-B) — " +
                "ya existe un lock activo (ejecución concurrente en curso, o pendiente de reconciliación manual " +
                "tras un intento previo interrumpido). No se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — no-repeat guard (XPAY-533 §2/§3/§5), sobre el PROPIO
        // directorio de Account B — nunca sobre el de Customer B.
        var (repeatOutcome, repeatArtifact) = M5AccountBArtifactReader.Resolve(accountCaptureDirFullPath);
        switch (repeatOutcome)
        {
            case M5AccountBArtifactReader.ResolutionOutcome.Found when repeatArtifact is not null && M5AccountBArtifactReader.IsValid(repeatArtifact):
                M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED_ALREADY_EXECUTED: ya existe un artefacto privado válido de Account B (M5-T1-B) " +
                    "de una ejecución previa exitosa. No se realizó ninguna llamada a Passport.");

            case M5AccountBArtifactReader.ResolutionOutcome.Found:
                M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED: existe un artefacto privado en el directorio de Account B (M5-T1-B) con " +
                    "formato o case_id inesperado — requiere revisión manual antes de continuar. " +
                    "No se realizó ninguna llamada a Passport.");

            case M5AccountBArtifactReader.ResolutionOutcome.MultipleArtifactsFound:
                M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED_AMBIGUOUS: se encontró más de un artefacto privado en el directorio de " +
                    "Account B (M5-T1-B) — requiere revisión manual antes de continuar. " +
                    "No se realizó ninguna llamada a Passport.");

            case M5AccountBArtifactReader.ResolutionOutcome.Unreadable:
                M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED: el directorio de Account B (M5-T1-B) contiene un artefacto privado " +
                    "ilegible/malformado — requiere revisión manual antes de continuar. " +
                    "No se realizó ninguna llamada a Passport.");

            case M5AccountBArtifactReader.ResolutionOutcome.DirectoryMissing:
            case M5AccountBArtifactReader.ResolutionOutcome.NoArtifactFound:
                break;
        }

        // Guard #4 — account_number dedicado de Account B.
        var accountNumber = configuration[EnvAccountNumber];
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{EnvAccountNumber} ausente al momento de intentar Create Account B (M5-T1-B). " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #5 — directorio privado de Customer B (M5-T1-A) legible,
        // READ-ONLY. Nunca se reutiliza como destino de escritura de Account B.
        var customerCaptureDirRaw = configuration[M5CustomerBPrivateCapture.EnvPrivateCaptureDir];
        var customerCaptureValidation = M5CustomerBPrivateCapture.Validate(customerCaptureDirRaw, repositoryRoot);
        if (customerCaptureValidation != M5CustomerBPrivateCapture.ValidationOutcome.Valid)
        {
            M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M5 Customer B ({M5CustomerBPrivateCapture.EnvPrivateCaptureDir}) inválido: {customerCaptureValidation}. " +
                "No se pudo obtener customer_id de M5-T1-A. No se realizó ninguna llamada a Passport.");
        }

        string customerCaptureDirFullPath;
        try
        {
            customerCaptureDirFullPath = Path.GetFullPath(customerCaptureDirRaw!, repositoryRoot);
        }
        catch (Exception ex)
        {
            M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el destino privado de Customer B: {ex.Message}");
        }

        var (customerResolutionOutcome, customerArtifact) = M5CustomerBArtifactReader.Resolve(customerCaptureDirFullPath);
        if (customerResolutionOutcome != M5CustomerBArtifactReader.ResolutionOutcome.Found
            || customerArtifact is null
            || !M5CustomerBArtifactReader.IsValid(customerArtifact))
        {
            M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el artefacto privado de Customer B (M5-T1-A): {customerResolutionOutcome}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        var customerId = customerArtifact.CustomerId!;

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            M5AccountBPrivateCapture.ReleaseLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        var request = new PassportLinkAccountRequest(CustomerId: customerId, AccountNumber: accountNumber);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE LinkAccountAsync — nunca
            // RetrieveAccountAsync ni ningún otro cliente Passport.
            var response = await customerAccountClient.LinkAccountAsync(request).ConfigureAwait(false);

            // XPAY-533 §10 — mismo criterio de ventana de crash que
            // Customer B: el lock sólo se libera si el artefacto se
            // capturó con éxito.
            var privateArtifactCaptured = TryWritePrivateArtifact(
                accountCaptureDirFullPath, commitSha, executedAtUtc, customerId, response);

            if (privateArtifactCaptured)
                M5AccountBPrivateCapture.ReleaseLock(lockFilePath);

            var evidence = CreateM5AccountBEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference,
                privateArtifactCaptured);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            M5AccountBPrivateCapture.ReleaseLock(lockFilePath);

            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateM5AccountBEvidenceBuilder.BuildPassportHttpFailure(
                request, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // Preserva customer_id (crudo, para trazabilidad Account B → Customer B)
    // y account_id (crudo, para un futuro Retrieve) — fuera del repo. Best-
    // effort DELIBERADO: Passport ya respondió — un fallo aquí nunca debe
    // perder el registro sanitizado ya obtenido.
    private static bool TryWritePrivateArtifact(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string customerId, PassportAccountResponse response)
    {
        try
        {
            var content = new Dictionary<string, object?>
            {
                ["case_id"]           = CreateM5AccountBEvidenceBuilder.CaseId,
                ["captured_at_utc"]    = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"] = commitSha,
                ["customer_id"]        = customerId,
                ["account_id"]         = response.Id,
                ["status_at_creation"] = response.Status,
            };

            M5AccountBPrivateCapture.Write(privateCaptureDirFullPath, "M5-T1-B", ToTimestampForPath(executedAtUtc), content);
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
