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
    // XPAY-351 — channel, transaction_purpose y terminal_label son campos
    // ESTRUCTURALES requeridos por el contrato productivo de Create QR Code
    // (ver PassportQrClient.Validate) — NO son datos sensibles ni
    // específicos de un recurso privado de Sandbox concreto (a diferencia de
    // key_id/customer_id, que sí identifican un recurso real y viven en
    // ~/.passport-sandbox.env). Se fijan aquí como constantes de
    // certificación, mismo criterio ya aplicado a
    // DisplayName="XPay Certification Test Key" en CreateKeyExecutor. No se
    // leen desde el entorno porque no son "targets": no identifican ningún
    // recurso privado, sólo satisfacen el contrato de protocolo.
    //
    // transaction_purpose="00" (Compras) — valor documentado más genérico
    // del conjunto confirmado (XPAY-297/298). XPAY-357 §8 investigó cambiar
    // este valor al semántico "PURCHASE" (visto en ejemplos oficiales
    // vigentes) — GATE DOCUMENTAL BLOQUEADO: la única aparición local de
    // "PURCHASE" en todo el repositorio es en el ejemplo de la RESPUESTA de
    // Decode QR Code (PassportDecodeQrCodeResponse.cs, endpoint y dirección
    // distintos), nunca como valor confirmado del REQUEST de Create QR ni
    // en ValidTransactionPurposes. No existe soporte contractual local
    // suficiente para incorporarlo sin ampliar una regla productiva
    // incierta — se mantiene "00" (TRANSACTION_PURPOSE_CHANGE_BLOCKED=YES).
    private const string CertificationTerminalLabel      = "XPAY-M4-T1-CERT";
    private const string CertificationTransactionPurpose = "00";

    // XPAY-360 — RESTAURADOS tras confirmación empírica: la ejecución real
    // de XPAY-359 (evidence-2026-09-16T00-33-15Z.json, sin vat, según la
    // corrección de XPAY-357) recibió HTTP 400 con
    // error_code=invalid_parameter_value, error_message="Field 'vat' is
    // required" — Passport Sandbox SÍ exige vat para STATIC, contradiciendo
    // el ejemplo oficial documentado que el director había revisado (que no
    // lo mostraba). El error confirma ÚNICAMENTE que vat es requerido — NO
    // confirma por sí mismo qué vat_type/vat_value/vat_base_value son
    // válidos. Estos tres valores concretos (FIXED/"0.00"/"0.00") se
    // restauran porque son EXACTAMENTE los que M4-T1 usaba antes de
    // XPAY-357, y siguen estando respaldados hoy por el contrato productivo
    // (PassportQrClient.Validate acepta cualquier PassportQrVatType definido
    // + cualquier vat_value/vat_base_value no vacíos) y por tests activos
    // (PassportQrClientTests.CreateQrCodeAsync_StaticWithVatExplicitlyIncluded_IsAllowed,
    // y el propio SyntheticVat() por defecto, usado también por DYNAMIC sin
    // cambios) — no una inferencia nueva ni un valor inventado para este
    // ticket. QUEDA PENDIENTE (fuera de alcance de XPAY-360, requiere
    // confirmación adicional de Passport/director): si vat_type=FIXED y
    // vat_value/vat_base_value="0.00" son la combinación que Passport
    // finalmente aceptará — el error de XPAY-359 no llegó a evaluar esos
    // subcampos porque vat estaba completamente ausente.
    private const string CertificationVatValue     = "0.00";
    private const string CertificationVatBaseValue = "0.00";

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
        //
        // XPAY-356 — Channel corregido de APP a POS: la ejecución real de
        // XPAY-354 (evidence-2026-09-15T23-06-08Z.json) usó APP y recibió
        // HTTP 400 de Passport. El RCA de XPAY-355 encontró que POS —no
        // APP— es el valor ya confirmado y probado para STATIC desde la
        // implementación histórica del cliente QR (ver
        // PassportQrClientTests.SyntheticStaticRequest(), que usa
        // Channel=POS desde XPAY-298), y coincide con el ejemplo oficial
        // STATIC vigente revisado por el director. Esta corrección NO
        // declara que APP haya sido la causa confirmada del HTTP 400 —
        // Passport nunca reveló el campo específico rechazado (gap de
        // diagnóstico documentado en XPAY-355) — es una corrección de
        // contrato basada en el valor STATIC ya confirmado localmente,
        // no una conclusión causal definitiva.
        //
        // XPAY-357 removió vat de este request (basándose en el ejemplo
        // oficial STATIC vigente, que no lo mostraba); XPAY-360 lo
        // RESTAURA tras confirmación empírica real de Passport Sandbox
        // (XPAY-359: HTTP 400, "Field 'vat' is required") — ver comentario
        // de CertificationVatValue/CertificationVatBaseValue arriba para el
        // razonamiento completo. amount/inc/qr_code_reference permanecen
        // deliberadamente AUSENTES, sin cambios respecto a XPAY-351/357.
        var request = new PassportCreateQrCodeRequest(
            KeyId: keyId,
            CustomerId: customerId,
            Type: PassportQrType.STATIC,
            Channel: PassportQrChannel.POS,
            AdditionalInfo: new PassportQrAdditionalInfoRequest(
                TransactionPurpose: CertificationTransactionPurpose,
                TerminalLabel: CertificationTerminalLabel))
        {
            Vat = new PassportQrVatRequest(
                VatType: PassportQrVatType.FIXED,
                VatValue: CertificationVatValue,
                VatBaseValue: CertificationVatBaseValue),
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
