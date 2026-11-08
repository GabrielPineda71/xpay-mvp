using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-522 — orquesta la ejecución REAL de M4-T6-C (Create QR Code —
// DYNAMIC — con customer_id INCORRECTO), reutilizando EXACTAMENTE el
// mismo generador sintético ya probado en M4-T3-C
// (InvalidCustomerIdGenerator.Generate() — UUID malformado, determinista,
// nunca un UUID v4 válido pero inexistente). Mirror estructural de
// CreateQrStaticInvalidCustomerExecutor (M4-T3-C), pero con el payload
// DYNAMIC (QrDynamicNegativeCaseFixture) — SIN modificar ni reutilizar
// código de CreateQrStaticInvalidCustomerExecutor ni de
// CreateQrDynamicExecutor (M4-T4, congelado).
//
// Precondición FAIL-CLOSED ADICIONAL (XPAY-522 §8): el fingerprint del
// key_id ACTIVO configurado debe coincidir EXACTAMENTE con el fingerprint
// esperado (9315E012C94C) — si no coincide, LocalBlocked, CERO HTTP.
//
// CASO NEGATIVO — el histórico STATIC (M4-T3-C) produjo HTTP 400/
// invalid_parameter_value/"UUID version 4 expected". Esto es contexto
// histórico, NUNCA una expectativa contractual codificada aquí para
// DYNAMIC — este executor sólo clasifica transporte, la evidencia
// registra factualmente lo observado (ver
// CreateQrDynamicInvalidCustomerEvidenceBuilder).
public static class CreateQrDynamicInvalidCustomerExecutor
{
    public const string ExpectedKeyIdFingerprint = "9315E012C94C";

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

        // XPAY-522 — key_id proviene de PASSPORT_TEST_QR_KEY_ID (la llave
        // ACTIVA de certificación, la MISMA de M4-T1/M4-T2/M4-T4/M4-T5) —
        // usada ÚNICAMENTE de forma READ/REFERENCE: este executor nunca la
        // muta. Es deliberadamente válida — lo único incorrecto en este
        // subcaso es customer_id.
        var keyId = configuration[HarnessTargetConfig.EnvQrKeyId];

        if (string.IsNullOrWhiteSpace(keyId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_KEY_ID ausente al momento de intentar Create QR Dynamic (M4-T6-C).");
        }

        // XPAY-522 §8 — precondición FAIL-CLOSED ADICIONAL: el key_id
        // activo configurado debe corresponder EXACTAMENTE al fingerprint
        // ya verificado en toda la certificación M4.
        var actualKeyIdFingerprint = Fingerprint.Compute(keyId);
        if (actualKeyIdFingerprint != expectedFingerprint)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_KEY_ID no coincide con el fingerprint esperado de la llave activa de " +
                "certificación. No se realizó ninguna llamada a Passport.");
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

        // XPAY-522 §2/§8 — customer_id NUNCA proviene de
        // PASSPORT_TEST_CUSTOMER_ID (el real) — SIEMPRE de
        // InvalidCustomerIdGenerator.Generate(), el MISMO mecanismo
        // sintético ya probado en M4-T3-C (nunca sustituido por un UUID v4
        // inexistente).
        var customerId = InvalidCustomerIdGenerator.Generate();

        var request = QrDynamicNegativeCaseFixture.BuildRequest(keyId, customerId);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateQrCodeAsync — nunca Decode,
            // Payment, Resolve, ni ninguna mutación de llave.
            var response = await qrClient.CreateQrCodeAsync(request).ConfigureAwait(false);
            var evidence = CreateQrDynamicInvalidCustomerEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateQrDynamicInvalidCustomerEvidenceBuilder.BuildPassportHttpFailure(
                request, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
