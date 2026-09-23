using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-506 — orquesta la ejecución REAL de M4-T4 (Create QR Code —
// DINÁMICO): lee los targets privados SOLO aquí (nunca en dry-run —
// HarnessOrchestrator.Prepare sólo confirma su PRESENCIA, jamás su valor),
// resuelve el commit SHA, construye el request usando ÚNICAMENTE el DTO
// productivo (PassportCreateQrCodeRequest), invoca
// IPassportQrClient.CreateQrCodeAsync EXACTAMENTE UNA VEZ, y clasifica el
// resultado. Nunca imprime nada por sí misma — devuelve datos ya saneados
// (o ninguno, en LocalBlocked). 100% testeable inyectando un
// IPassportQrClient/ICommitShaProvider fake.
//
// Mirror ESTRUCTURAL de CreateQrStaticExecutor (M4-T1, XPAY-351/458/460) —
// MISMOS targets (PASSPORT_TEST_QR_KEY_ID / PASSPORT_TEST_CUSTOMER_ID),
// MISMO cliente (IPassportQrClient, sin acceso a ninguna API de mutación
// de llaves), MISMA disciplina de una sola llamada — ÚNICA diferencia
// deliberada: Type=DYNAMIC en vez de STATIC, con los ajustes de payload
// que ese tipo exige según PassportQrClient.Validate() (ver comentarios
// del request abajo). Reutiliza KeyOperationResult/KeyOperationOutcome sin
// tipo dedicado nuevo, mismo criterio que CreateQrStaticExecutor.
public static class CreateQrDynamicExecutor
{
    // XPAY-506 — mismos valores de certificación YA usados y aceptados por
    // Passport para M4-T1 (Gustavo confirmó que vat/inc/tip son
    // "características informativas dentro de Bre-B y no se aplican al
    // valor total", XPAY-460 §7) — se reutilizan sin cambios para DYNAMIC,
    // no se inventa un valor nuevo sin respaldo. La única razón por la que
    // vat es estrictamente obligatorio aquí (a diferencia de STATIC, donde
    // es opcional) es el contrato ya confirmado en
    // PassportQrClient.Validate(): "vat es requerido cuando type es
    // DYNAMIC".
    private const string CertificationVatValue     = "100.00";
    private const string CertificationVatBaseValue = "100.00";
    private const string CertificationIncValue     = "10.00";
    private const string CertificationTipValue     = "100.00";

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

        // XPAY-506 — mismos targets EXACTOS que M4-T1 (XPAY-460):
        // PASSPORT_TEST_QR_KEY_ID (llave ACTIVE protegida) +
        // PASSPORT_TEST_CUSTOMER_ID (customer válido normal de
        // certificación). NUNCA PASSPORT_TEST_NEW_KEY_ID/_VALUE
        // (contaminadas, XPAY-487/488/491), PASSPORT_TEST_QR_SUSPENDED_KEY_ID
        // (M4-T3-A) ni PASSPORT_TEST_QR_DELETED_KEY_ID (M4-T3-B) — sin
        // fallback hacia ninguna de ellas, ni siquiera si están presentes.
        var keyId      = configuration[HarnessTargetConfig.EnvQrKeyId];
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s) al momento de intentar Create QR Dynamic.");
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

        // XPAY-506 — M4-T4: type=DYNAMIC. Payload MÍNIMO y determinista,
        // sustentado ÚNICAMENTE por el contrato ya confirmado en
        // PassportQrClient.Validate() (XPAY-505 auditó exactamente estas
        // reglas):
        //   - vat: REQUERIDO cuando type=DYNAMIC (a diferencia de STATIC,
        //     donde es opcional) — SIEMPRE se envía aquí.
        //   - amount: deliberadamente AUSENTE — sigue siendo opcional para
        //     DYNAMIC (un QR dinámico sin monto predefinido es un escenario
        //     válido y estructuralmente más simple); no existe en este
        //     repositorio ningún valor de "amount" de certificación ya
        //     confirmado/empírico que reutilizar, y esta implementación
        //     NO inventa uno sin respaldo (XPAY-506 §5).
        //   - inc: al no haber amount, la regla "inc requerido si amount
        //     está presente" no aplica — se envía de todas formas,
        //     reutilizando el mismo valor ya confirmado para M4-T1, para
        //     mantener el payload lo más cercano posible a un shape ya
        //     validado por Passport, cambiando ÚNICAMENTE el campo `type`.
        //   - tip: mismo criterio que inc.
        //   - additional_info: deliberadamente AUSENTE — opcional en ambos
        //     tipos; M4-T1 tampoco lo envía en su versión vigente
        //     (XPAY-458).
        //   - channel=MPOS: mismo valor ya confirmado funcional para M4-T1.
        //   - qr_code_reference: QrCodeReferenceGenerator (XPAY-458),
        //     fresca y única por ejecución — nunca reutilizada entre
        //     ejecuciones ni con M4-T1/M4-T3.
        var request = new PassportCreateQrCodeRequest(
            KeyId: keyId,
            CustomerId: customerId,
            Type: PassportQrType.DYNAMIC,
            Channel: PassportQrChannel.MPOS)
        {
            Vat = new PassportQrVatRequest(
                VatType: PassportQrVatType.FIXED,
                VatValue: CertificationVatValue,
                VatBaseValue: CertificationVatBaseValue),
            Inc = new PassportQrIncRequest(
                IncType: PassportQrVatType.FIXED,
                IncValue: CertificationIncValue),
            Tip = new PassportQrTipRequest(
                TipType: PassportQrVatType.FIXED,
                TipValue: CertificationTipValue),
            QrCodeReference = QrCodeReferenceGenerator.Generate(),
        };

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateQrCodeAsync — nunca
            // DecodeQrCodeAsync ni ningún método de IPassportKeyClient (esta
            // clase ni siquiera recibe una instancia de ese cliente).
            var response = await qrClient.CreateQrCodeAsync(request).ConfigureAwait(false);
            var evidence = CreateQrDynamicEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateQrDynamicEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
