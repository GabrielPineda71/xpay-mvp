using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-522 — request DYNAMIC/MPOS del fixture de certificación M4,
// factorizado para reutilizarse EXACTAMENTE igual en los tres executors
// negativos de M4-T6 (suspended-key/deleted-key/invalid-customer), SIN
// duplicar literales entre archivos y SIN tocar CreateQrDynamicExecutor.cs
// (el contrato de M4-T4, ya congelado y con SUCCESS real, permanece
// deliberadamente intacto — este archivo es nuevo, no una refactorización
// de aquel). Mismo criterio de aislamiento ya aplicado por
// QrStaticCertificationFixture (XPAY-471) frente a CreateQrStaticExecutor.
//
// Valores IDÉNTICOS a los ya confirmados y usados en la ejecución real
// SUCCESS de M4-T4 (XPAY-506/512/513): vat FIXED/"100.00"/"100.00", inc
// FIXED/"10.00", tip FIXED/"100.00", amount "10000.00"/"COP" (valor
// oficial del ejemplo Postman de Passport, XPAY-512). Type/channel =
// DYNAMIC/MPOS, sin additional_info (mismo contrato M4-T4).
// qr_code_reference reutiliza QrCodeReferenceGenerator (XPAY-458) — NUEVA
// y única por ejecución, nunca reutilizada entre subcasos ni con M4-T4/M4-T5.
internal static class QrDynamicNegativeCaseFixture
{
    private const string VatValue        = "100.00";
    private const string VatBaseValue    = "100.00";
    private const string IncValue        = "10.00";
    private const string TipValue        = "100.00";
    private const string AmountValue     = "10000.00";
    private const string AmountCurrency  = "COP";

    public static PassportCreateQrCodeRequest BuildRequest(string keyId, string customerId) => new(
        KeyId: keyId,
        CustomerId: customerId,
        Type: PassportQrType.DYNAMIC,
        Channel: PassportQrChannel.MPOS)
    {
        Vat = new PassportQrVatRequest(
            VatType: PassportQrVatType.FIXED,
            VatValue: VatValue,
            VatBaseValue: VatBaseValue),
        Amount = new PassportQrAmountRequest(AmountValue)
        {
            Currency = AmountCurrency,
        },
        Inc = new PassportQrIncRequest(
            IncType: PassportQrVatType.FIXED,
            IncValue: IncValue),
        Tip = new PassportQrTipRequest(
            TipType: PassportQrVatType.FIXED,
            TipValue: TipValue),
        QrCodeReference = QrCodeReferenceGenerator.Generate(),
    };
}
