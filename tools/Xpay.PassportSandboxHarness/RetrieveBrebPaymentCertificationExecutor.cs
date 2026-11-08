using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-529 — orquesta la ejecución REAL futura de la certificación M5
// Retrieve Bre-B Payment (GET /v1/payments/{payment_id}), reutilizando
// EXCLUSIVAMENTE el payment_id preservado privadamente por
// CreateBrebPaymentCertificationExecutor — NUNCA crea un Payment nuevo,
// NUNCA acepta un payment_id hardcoded ni histórico (XPAY-529 §9).
//
// AISLAMIENTO ARQUITECTÓNICO — mismo criterio que el Create: llama
// EXCLUSIVAMENTE a IPassportPaymentClient.RetrievePaymentAsync, nunca a
// BrebPaymentService/BD/reconciliación.
//
// Reutiliza el MISMO destino privado que Create
// (M5PaymentPrivateCapture.EnvPrivateCaptureDir) — aquí SÓLO en modo
// lectura (M5PaymentArtifactReader), nunca escribe en ese directorio.
public static class RetrieveBrebPaymentCertificationExecutor
{
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

        var captureDirRaw = configuration[M5PaymentPrivateCapture.EnvPrivateCaptureDir];
        var validation = M5PaymentPrivateCapture.Validate(captureDirRaw, repositoryRoot);
        if (validation != M5PaymentPrivateCapture.ValidationOutcome.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M5 Payment ({M5PaymentPrivateCapture.EnvPrivateCaptureDir}) inválido: {validation}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        var captureDirFullPath = Path.GetFullPath(captureDirRaw!, repositoryRoot);
        var (resolutionOutcome, artifact) = M5PaymentArtifactReader.Resolve(captureDirFullPath);
        if (resolutionOutcome != M5PaymentArtifactReader.ResolutionOutcome.Found || artifact is null)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el artefacto privado M5 Create Payment: {resolutionOutcome}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        var accountId = configuration[PassportOptions.EnvOperationalAccountId];
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{PassportOptions.EnvOperationalAccountId} ausente al momento de intentar Retrieve Payment (M5). " +
                "No se realizó ninguna llamada a Passport.");
        }

        var expectedAccountIdFingerprint = Fingerprint.Compute(accountId);
        if (!M5PaymentArtifactReader.IdentityMatches(artifact, expectedAccountIdFingerprint))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El artefacto privado M5 Create Payment no coincide con la identidad esperada " +
                "(case_id/account_id/payment_id) — bloqueado por seguridad. No se realizó ninguna llamada a Passport.");
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

        var paymentId = artifact.PaymentId!; // ya validado no-vacío por IdentityMatches.

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE RetrievePaymentAsync — nunca
            // CreateBrebPaymentAsync ni ningún otro cliente Passport.
            var response = await paymentClient.RetrievePaymentAsync(paymentId).ConfigureAwait(false);
            var evidence = RetrieveBrebPaymentCertificationEvidenceBuilder.BuildSuccess(
                paymentId, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = RetrieveBrebPaymentCertificationEvidenceBuilder.BuildPassportHttpFailure(
                paymentId, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
