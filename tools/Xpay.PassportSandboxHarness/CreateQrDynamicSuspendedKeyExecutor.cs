using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-522 — orquesta la ejecución REAL de M4-T6-A (Create QR Code —
// DYNAMIC — con llave Bre-B SUSPENDIDA), reutilizando EXCLUSIVAMENTE la
// fixture suspendida ya existente de M4-T3-A (PASSPORT_TEST_QR_SUSPENDED_KEY_ID)
// — NUNCA suspende una llave nueva, NUNCA muta la llave de ninguna forma.
// Mirror estructural de CreateQrStaticSuspendedKeyExecutor (M4-T3-A), pero
// con el payload DYNAMIC (QrDynamicNegativeCaseFixture) en vez de STATIC —
// SIN modificar ni reutilizar código de CreateQrStaticSuspendedKeyExecutor
// ni de CreateQrDynamicExecutor (M4-T4, congelado).
//
// Precondición FAIL-CLOSED ADICIONAL (XPAY-522 §6, más estricta que su
// contraparte STATIC): el fingerprint del key_id configurado debe
// coincidir EXACTAMENTE con el fingerprint histórico ya verificado de la
// fixture suspendida (E2B083E0FD76) — si no coincide, LocalBlocked, CERO
// HTTP. Esto evita gastar la única llamada real autorizada apuntando por
// error a una llave distinta de la ya confirmada como suspendida.
//
// CASO NEGATIVO — un "caso negativo" NO implica que Create QR deba fallar.
// El histórico STATIC (M4-T3-A) produjo QR ACTIVE y Passport confirmó que
// es el comportamiento esperado (rechazo en Payment, no en Create QR).
// Este executor NUNCA decide si el resultado DYNAMIC "debería" ser
// distinto — sólo clasifica transporte (Success/PassportFailure/LocalBlocked)
// y deja la interpretación de certificación a la evidencia/revisión
// posterior (ver CreateQrDynamicSuspendedKeyEvidenceBuilder).
public static class CreateQrDynamicSuspendedKeyExecutor
{
    public const string ExpectedKeyIdFingerprint = "E2B083E0FD76";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportQrClient qrClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? expectedKeyIdFingerprintOverride = null)
    {
        // XPAY-522 — override EXCLUSIVO de tests offline: SHA-256 no es
        // reversible, así que un test no puede construir un key_id
        // sintético cuyo fingerprint coincida con el valor histórico real
        // (ExpectedKeyIdFingerprint) sin conocerlo de antemano. En
        // producción este parámetro SIEMPRE es null — el valor
        // efectivamente comparado es la constante real.
        var expectedFingerprint = expectedKeyIdFingerprintOverride ?? ExpectedKeyIdFingerprint;
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(qrClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        // XPAY-522 — key_id proviene EXCLUSIVAMENTE de
        // PASSPORT_TEST_QR_SUSPENDED_KEY_ID (misma fixture desechable de
        // M4-T3-A, reutilizada de forma READ-ONLY — nunca suspendida de
        // nuevo, nunca mutada). PROHIBIDO cualquier fallback hacia
        // PASSPORT_TEST_QR_KEY_ID (llave ACTIVA) o PASSPORT_TEST_QR_DELETED_KEY_ID
        // (target exclusivo de M4-T6-B).
        var keyId      = configuration[HarnessTargetConfig.EnvQrSuspendedKeyId];
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_SUSPENDED_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s) al momento de intentar Create QR Dynamic (M4-T6-A).");
        }

        // XPAY-522 §6 — precondición FAIL-CLOSED ADICIONAL: el key_id
        // configurado debe corresponder EXACTAMENTE a la fixture suspendida
        // ya verificada (mismo fingerprint que M4-T3-A). Nunca imprime el
        // valor crudo ni el fingerprint calculado en el mensaje de bloqueo.
        var actualKeyIdFingerprint = Fingerprint.Compute(keyId);
        if (actualKeyIdFingerprint != expectedFingerprint)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_SUSPENDED_KEY_ID no coincide con el fingerprint esperado de la fixture " +
                "suspendida histórica (M4-T3-A). No se realizó ninguna llamada a Passport.");
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
            var evidence = CreateQrDynamicSuspendedKeyEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateQrDynamicSuspendedKeyEvidenceBuilder.BuildPassportHttpFailure(
                request, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
