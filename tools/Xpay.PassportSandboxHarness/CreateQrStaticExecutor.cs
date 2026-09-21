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
    // XPAY-458 — respuesta oficial de Passport (Gustavo, 2026-09-21)
    // confirmó, para M4-T1 (STATIC sin monto), un request funcional que NO
    // incluye additional_info/transaction_purpose/terminal_label en
    // absoluto — contradice el supuesto previo (XPAY-351, ver historial de
    // constantes eliminadas abajo) de que esos campos eran estructuralmente
    // requeridos por el contrato productivo. PassportQrClient.Validate ya
    // no los exige incondicionalmente (ver XPAY-458 en
    // PassportCreateQrCodeRequest.cs/PassportQrClient.cs) — este executor
    // simplemente deja de construir AdditionalInfo para M4-T1.
    //
    // Historial (constantes eliminadas en XPAY-458, documentado aquí para
    // no perder el rastro): CertificationTerminalLabel="XPAY-M4-T1-CERT",
    // CertificationTransactionPurpose="00" — usadas desde XPAY-351 hasta
    // XPAY-452 (evidence-2026-09-16T00-56-00Z.json, el 3er intento fallido,
    // aún las incluía).
    //
    // XPAY-460 — VALORES ACTUALIZADOS: Gustavo confirmó posteriormente que
    // vat/inc/tip son "características informativas dentro de Bre-B y no se
    // aplican al valor total", y que sus valores concretos "dependen de la
    // evaluación comercial/contable de XPAY" — es decir, Passport no exige
    // un valor específico, sólo la presencia estructural del campo (ya
    // confirmada empíricamente: XPAY-360 para vat, XPAY-458 para inc —
    // evidence-2026-09-16T00-33-15Z.json/evidence-2026-09-16T00-56-00Z.json).
    // Para el FIXTURE DE CERTIFICACIÓN de M4-T1 se adoptan explícitamente
    // los mismos valores del ejemplo funcional que Passport entregó (SOLO
    // como valores de harness de certificación — NUNCA como regla financiera
    // productiva; PassportQrClient.Validate NO exige estos importes
    // específicos, ver XPAY-460 §7): vat FIXED/"100.00"/"100.00", inc
    // FIXED/"10.00", tip FIXED/"100.00". Reemplazan los "0.00" de XPAY-458
    // (elegidos entonces por analogía sin respaldo propio — ver auditoría
    // XPAY-459, VAT/INC/TIP_EVIDENCE_STRENGTH).
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

        // XPAY-460 — key_id proviene EXCLUSIVAMENTE de PASSPORT_TEST_QR_KEY_ID
        // (la Key ACTIVE de tipo Business Entity Code verificada por el
        // director en Passport Sandbox Dashboard), NUNCA de
        // PASSPORT_TEST_NEW_KEY_ID (la llave DELETED de M3) — sin fallback,
        // ni siquiera si esta última está presente. Ver
        // HarnessTargetConfig.AllPresentForCreateQrStatic (misma regla ya
        // aplicada en el preflight, esto es defensa en profundidad).
        var keyId      = configuration[HarnessTargetConfig.EnvQrKeyId];
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s) al momento de intentar Create QR Static.");
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
        // corregido aquí).
        //
        // XPAY-356 — Channel corregido de APP a POS en su momento (RCA de
        // XPAY-355 tras el HTTP 400 de XPAY-354). XPAY-458 lo corrige de
        // nuevo, esta vez de POS a MPOS: el ejemplo funcional confirmado por
        // Passport (Gustavo, 2026-09-21) específicamente para M4-T1 usa
        // channel=MPOS. A diferencia de la corrección XPAY-356 (basada en
        // inferencia, sin que Passport revelara el campo rechazado), ésta
        // proviene directamente de un ejemplo que Passport confirmó como
        // funcional — no es una inferencia.
        //
        // XPAY-357 removió vat de este request; XPAY-360 lo restauró tras
        // confirmación empírica (HTTP 400 "Field 'vat' is required"). XPAY-458
        // agrega ahora inc (mismo tipo de confirmación empírica: HTTP 400
        // "Field 'inc' is required", evidence-2026-09-16T00-56-00Z.json) y
        // tip (nuevo, sin evidencia empírica previa — incluido porque el
        // ejemplo funcional de Gustavo lo trae), y agrega qr_code_reference
        // (NUEVA y única por ejecución — ver QrCodeReferenceGenerator; nunca
        // reutiliza una referencia de un intento anterior). additional_info
        // queda deliberadamente AUSENTE (ver comentario de las constantes
        // eliminadas arriba) — amount permanece deliberadamente AUSENTE, sin
        // cambios respecto a XPAY-351.
        var request = new PassportCreateQrCodeRequest(
            KeyId: keyId,
            CustomerId: customerId,
            Type: PassportQrType.STATIC,
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
            //
            // XPAY-358 — si la excepción es un PassportTransportException,
            // ya trae diagnóstico estructurado y PRE-SANEADO (StatusCode/
            // SafeErrorCode/SafeErrorMessage, vía
            // PassportErrorBodySanitizer) — se reenvía tal cual, sin volver
            // a sanitizar aquí (esta clase no es responsable de sanitizar,
            // sólo de transportar lo que ya llegó seguro). Las otras dos
            // excepciones (Authentication/Protocol) no tienen este
            // diagnóstico — se dejan en null, igual que antes.
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateQrStaticEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
