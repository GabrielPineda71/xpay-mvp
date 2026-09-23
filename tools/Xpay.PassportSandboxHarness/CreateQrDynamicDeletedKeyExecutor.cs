using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-522 — orquesta la ejecución REAL de M4-T6-B (Create QR Code —
// DYNAMIC — con llave Bre-B ELIMINADA), reutilizando EXCLUSIVAMENTE la
// referencia de la fixture eliminada ya existente de M4-T3-B
// (PASSPORT_TEST_QR_DELETED_KEY_ID) — NUNCA crea ni elimina otra llave.
// Mirror estructural de CreateQrStaticDeletedKeyExecutor (M4-T3-B), pero
// con el payload DYNAMIC (QrDynamicNegativeCaseFixture) — SIN modificar ni
// reutilizar código de CreateQrStaticDeletedKeyExecutor ni de
// CreateQrDynamicExecutor (M4-T4, congelado).
//
// Precondición FAIL-CLOSED ADICIONAL (XPAY-522 §7): el fingerprint del
// key_id configurado debe coincidir EXACTAMENTE con el fingerprint
// histórico ya verificado de la fixture eliminada (8A15DB69E7E0) — si no
// coincide, LocalBlocked, CERO HTTP.
//
// CASO NEGATIVO — el histórico STATIC (M4-T3-B) produjo HTTP 404/
// resource_not_found/"key not found.". Esto es contexto histórico, NUNCA
// una expectativa contractual codificada aquí para DYNAMIC — este
// executor sólo clasifica transporte, la evidencia registra factualmente
// lo observado (ver CreateQrDynamicDeletedKeyEvidenceBuilder).
public static class CreateQrDynamicDeletedKeyExecutor
{
    public const string ExpectedKeyIdFingerprint = "8A15DB69E7E0";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportQrClient qrClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? expectedKeyIdFingerprintOverride = null)
    {
        // XPAY-522 — override EXCLUSIVO de tests offline (ver
        // CreateQrDynamicSuspendedKeyExecutor para el razonamiento
        // completo). En producción SIEMPRE es null.
        var expectedFingerprint = expectedKeyIdFingerprintOverride ?? ExpectedKeyIdFingerprint;
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(qrClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        // XPAY-522 — key_id proviene EXCLUSIVAMENTE de
        // PASSPORT_TEST_QR_DELETED_KEY_ID (misma fixture desechable de
        // M4-T3-B, ya eliminada — usada aquí sólo como referencia; este
        // executor NUNCA invoca Create/Delete Key). PROHIBIDO cualquier
        // fallback hacia PASSPORT_TEST_QR_KEY_ID (llave ACTIVA) o
        // PASSPORT_TEST_QR_SUSPENDED_KEY_ID (target exclusivo de M4-T6-A).
        var keyId      = configuration[HarnessTargetConfig.EnvQrDeletedKeyId];
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];

        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_DELETED_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s) al momento de intentar Create QR Dynamic (M4-T6-B).");
        }

        // XPAY-522 §7 — precondición FAIL-CLOSED ADICIONAL: el key_id
        // configurado debe corresponder EXACTAMENTE a la fixture eliminada
        // ya verificada (mismo fingerprint que M4-T3-B).
        var actualKeyIdFingerprint = Fingerprint.Compute(keyId);
        if (actualKeyIdFingerprint != expectedFingerprint)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_DELETED_KEY_ID no coincide con el fingerprint esperado de la fixture " +
                "eliminada histórica (M4-T3-B). No se realizó ninguna llamada a Passport.");
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

        var request = QrDynamicNegativeCaseFixture.BuildRequest(keyId, customerId);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateQrCodeAsync — nunca Decode,
            // Payment, Resolve, ni ninguna mutación de llave.
            var response = await qrClient.CreateQrCodeAsync(request).ConfigureAwait(false);
            var evidence = CreateQrDynamicDeletedKeyEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateQrDynamicDeletedKeyEvidenceBuilder.BuildPassportHttpFailure(
                request, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
