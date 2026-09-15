using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-351 — orquesta la ejecución REAL de M4-T1 (Create QR Code —
// ESTÁTICO): lee los targets privados SOLO aquí (nunca en dry-run —
// HarnessOrchestrator.Prepare sólo confirma su PRESENCIA, jamás su valor),
// resuelve el commit SHA, construye el request usando ÚNICAMENTE el DTO
// productivo (PassportCreateQrCodeRequest), invoca
// IPassportQrClient.CreateQrCodeAsync EXACTAMENTE UNA VEZ, y clasifica el
// resultado. Nunca imprime nada por sí misma — devuelve datos ya saneados
// (o ninguno, en LocalBlocked). 100% testeable inyectando un
// IPassportQrClient/ICommitShaProvider fake.
//
// Reutiliza el mismo tipo de resultado que Resolve/Suspend/Activate/Delete
// Key (KeyOperationResult/KeyOperationOutcome) — la clasificación de 3
// salidas (LocalBlocked/Success/PassportFailure) es exactamente la misma
// semántica aquí, sin necesidad de un tipo dedicado nuevo (a diferencia de
// CreateKeyExecutor, que mantiene su propio tipo histórico — ver
// KeyOperationResult.cs).
public static class CreateQrStaticExecutor
{
    // XPAY-351 — vat_type/vat_value/vat_base_value, channel,
    // transaction_purpose y terminal_label son campos ESTRUCTURALES
    // requeridos por el contrato productivo de Create QR Code (ver
    // PassportQrClient.Validate) — NO son datos sensibles ni específicos de
    // un recurso privado de Sandbox concreto (a diferencia de
    // key_id/customer_id, que sí identifican un recurso real y viven en
    // ~/.passport-sandbox.env). Se fijan aquí como constantes de
    // certificación, mismo criterio ya aplicado a
    // DisplayName="XPay Certification Test Key" en CreateKeyExecutor. No se
    // leen desde el entorno porque no son "targets": no identifican ningún
    // recurso privado, sólo satisfacen el contrato de protocolo.
    //
    // transaction_purpose="00" (Compras) — valor documentado más genérico
    // del conjunto confirmado (XPAY-297/298).
    // vat_type=FIXED con vat_value/vat_base_value="0.00" — QR de
    // certificación SIN monto asociado (M4-T1 es QR ESTÁTICO, sin amount);
    // se usa un valor fijo en cero como base neutral, no una tarifa/monto
    // real — nunca se inventa un monto de transacción para este caso.
    private const string CertificationTerminalLabel      = "XPAY-M4-T1-CERT";
    private const string CertificationTransactionPurpose = "00";
    private const string CertificationVatValue           = "0.00";
    private const string CertificationVatBaseValue       = "0.00";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportQrClient qrClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(qrClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        var keyId      = configuration[HarnessTargetConfig.EnvNewKeyId];
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_NEW_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s) al momento de intentar Create QR Static.");
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

        // XPAY-351 §7 — M4-T1: type=STATIC, amount deliberadamente AUSENTE
        // (no se construye PassportQrAmountRequest en absoluto) — esto es
        // una decisión de ESTE caso de certificación, no un cambio a la
        // política general del cliente productivo (que hoy tolera
        // STATIC+amount sin bloquearlo — CONTRACT_GAP_1 de XPAY-350, no
        // corregido aquí). §8 — qr_code_reference NO se usa: no es
        // necesario para Create QR (es opcional) y XPAY-351 prefiere
        // explícitamente no usarlo.
        var request = new PassportCreateQrCodeRequest(
            KeyId: keyId,
            CustomerId: customerId,
            Type: PassportQrType.STATIC,
            Channel: PassportQrChannel.APP,
            AdditionalInfo: new PassportQrAdditionalInfoRequest(
                TransactionPurpose: CertificationTransactionPurpose,
                TerminalLabel: CertificationTerminalLabel),
            Vat: new PassportQrVatRequest(
                VatType: PassportQrVatType.FIXED,
                VatValue: CertificationVatValue,
                VatBaseValue: CertificationVatBaseValue));

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateQrCodeAsync — nunca
            // DecodeQrCodeAsync ni ningún método de IPassportKeyClient.
            var response = await qrClient.CreateQrCodeAsync(request).ConfigureAwait(false);
            var evidence = CreateQrStaticEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // Mensajes ya saneados por diseño (PassportHttpClient/
            // PassportQrClient): nunca incluyen body, token ni
            // Authorization, ni key_id/customer_id/qr_code_data.
            var evidence = CreateQrStaticEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus: null, commitSha, executedAtUtc);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
