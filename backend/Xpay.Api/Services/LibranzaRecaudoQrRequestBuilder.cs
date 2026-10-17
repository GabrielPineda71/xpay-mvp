using System.Globalization;
using Xpay.Api.Common;
using Xpay.Api.Integrations.Passport;

namespace Xpay.Api.Services;

// P0 anticipo - construcción PURA del Create QR Code (POST /v1/qrcodes) de un
// lote de recaudo. Sin red ni BD: todo fallo ocurre ANTES de cualquier HTTP.
//
// Contrato (PassportCreateQrCodeRequest/PassportQrClient, certificado en
// Sandbox M4-T4 con CreateQrDynamicExecutor):
//   type DYNAMIC + channel MPOS (el canal certificado);
//   amount  = total EXACTO del lote, "F2" invariante, COP;
//   qr_code_reference = referencia opaca del lote (<= 17, alfanumérica, sin 'P');
//   vat (obligatorio en DYNAMIC) = FIXED: IVA total del lote sobre la base de
//   comisiones; inc (obligatorio con amount en DYNAMIC) = FIXED 0.00;
//   sin tip: una propina alteraría el monto recibido.
public static class LibranzaRecaudoQrRequestBuilder
{
    public static PassportCreateQrCodeRequest Build(
        string referencia, decimal montoTotal, decimal totalComision, decimal totalIva,
        string? collectionKeyId, string? customerId)
    {
        if (string.IsNullOrWhiteSpace(collectionKeyId))
            throw new PassportConfigurationException($"{PassportOptions.EnvCollectionKeyId} no está configurado.");
        if (string.IsNullOrWhiteSpace(customerId))
            throw new PassportConfigurationException($"{PassportOptions.EnvOperationalCustomerId} no está configurado.");
        if (!LibranzaRecaudoReferencia.EsValida(referencia))
            throw new InvalidOperationException("La referencia del lote no cumple el contrato de qr_code_reference.");
        if (montoTotal <= 0 || totalComision < 0 || totalIva < 0)
            throw new InvalidOperationException("Montos del lote inválidos para generar el QR.");

        return new PassportCreateQrCodeRequest(
            KeyId:      collectionKeyId.Trim(),
            CustomerId: customerId.Trim(),
            Type:       PassportQrType.DYNAMIC,
            Channel:    PassportQrChannel.MPOS)
        {
            QrCodeReference = referencia,
            Amount = new PassportQrAmountRequest(Monto(montoTotal)) { Currency = "COP" },
            Vat    = new PassportQrVatRequest(PassportQrVatType.FIXED, Monto(totalIva), Monto(totalComision)),
            Inc    = new PassportQrIncRequest(PassportQrVatType.FIXED, Monto(0m)),
        };
    }

    public static string Monto(decimal valor) => valor.ToString("F2", CultureInfo.InvariantCulture);

    // Parseo estricto de un monto Passport ("10000.00"). null si no es válido.
    public static decimal? ParsearMonto(string? valor) =>
        decimal.TryParse(valor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
}
