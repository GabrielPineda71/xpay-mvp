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
//   - M5CustomerBPrivateCapture.EnvPrivateCaptureDir — READ-ONLY aquí (el
//     mismo directorio que M5-T1-A ya usó para escribir). Ausente/insuficiente
//     bloquea localmente.
//   - PASSPORT_TEST_M5_ACCOUNT_B_ACCOUNT_NUMBER — dedicada, harness-only.
//     XPAY-531 §11 prohíbe explícitamente usar PASSPORT_ACCOUNT_ID (la
//     cuenta operativa de XPAY) como account_number de Account B.
//   - M5AccountBPrivateCapture.EnvPrivateCaptureDir — FAIL-CLOSED antes de
//     HTTP, mismo criterio que los demás módulos de captura privada.
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

        // Guard #1 — account_number dedicado de Account B.
        var accountNumber = configuration[EnvAccountNumber];
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{EnvAccountNumber} ausente al momento de intentar Create Account B (M5-T1-B). " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #2 — directorio privado de Customer B (M5-T1-A) legible,
        // READ-ONLY. Nunca se reutiliza como destino de escritura de Account B.
        var customerCaptureDirRaw = configuration[M5CustomerBPrivateCapture.EnvPrivateCaptureDir];
        var customerCaptureValidation = M5CustomerBPrivateCapture.Validate(customerCaptureDirRaw, repositoryRoot);
        if (customerCaptureValidation != M5CustomerBPrivateCapture.ValidationOutcome.Valid)
        {
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
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el destino privado de Customer B: {ex.Message}");
        }

        var (resolutionOutcome, artifact) = M5CustomerBArtifactReader.Resolve(customerCaptureDirFullPath);
        if (resolutionOutcome != M5CustomerBArtifactReader.ResolutionOutcome.Found
            || artifact is null
            || !M5CustomerBArtifactReader.IsValid(artifact))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el artefacto privado de Customer B (M5-T1-A): {resolutionOutcome}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        var customerId = artifact.CustomerId!;

        // Guard #3 — destino privado de captura de Account B, FAIL-CLOSED
        // antes de HTTP (mismo criterio que los demás módulos).
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

        var request = new PassportLinkAccountRequest(CustomerId: customerId, AccountNumber: accountNumber);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE LinkAccountAsync — nunca
            // RetrieveAccountAsync ni ningún otro cliente Passport.
            var response = await customerAccountClient.LinkAccountAsync(request).ConfigureAwait(false);

            var privateArtifactCaptured = TryWritePrivateArtifact(
                accountCaptureDirFullPath, commitSha, executedAtUtc, customerId, response);

            var evidence = CreateM5AccountBEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference,
                privateArtifactCaptured);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
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
