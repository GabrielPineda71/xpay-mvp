using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-471 — request STATIC/MPOS del fixture de certificación M4,
// factorizado para reutilizarse EXACTAMENTE igual en los tres executors
// negativos de M4-T3 (suspended-key/deleted-key/invalid-customer), SIN
// duplicar literales entre archivos y SIN tocar CreateQrStaticExecutor.cs
// (el contrato de M4-T1 permanece deliberadamente intacto — este archivo es
// nuevo, no una refactorización de aquel).
//
// Valores IDÉNTICOS a los ya confirmados y usados en la ejecución real de
// M4-T1/M4-T2 (XPAY-460): vat FIXED/"100.00"/"100.00", inc FIXED/"10.00",
// tip FIXED/"100.00" — mismo fixture de certificación, misma justificación
// (Passport aclaró que son informativos, no afectan el valor total). Type/
// channel = STATIC/MPOS, sin amount/additional_info (mismo contrato M4-T1).
// qr_code_reference reutiliza QrCodeReferenceGenerator (XPAY-458) — NUEVA y
// única por ejecución, nunca reutilizada entre subcasos ni con M4-T1/M4-T2.
internal static class QrStaticCertificationFixture
{
    private const string VatValue     = "100.00";
    private const string VatBaseValue = "100.00";
    private const string IncValue     = "10.00";
    private const string TipValue     = "100.00";

    public static PassportCreateQrCodeRequest BuildRequest(string keyId, string customerId) => new(
        KeyId: keyId,
        CustomerId: customerId,
        Type: PassportQrType.STATIC,
        Channel: PassportQrChannel.MPOS)
    {
        Vat = new PassportQrVatRequest(
            VatType: PassportQrVatType.FIXED,
            VatValue: VatValue,
            VatBaseValue: VatBaseValue),
        Inc = new PassportQrIncRequest(
            IncType: PassportQrVatType.FIXED,
            IncValue: IncValue),
        Tip = new PassportQrTipRequest(
            TipType: PassportQrVatType.FIXED,
            TipValue: TipValue),
        QrCodeReference = QrCodeReferenceGenerator.Generate(),
    };
}
