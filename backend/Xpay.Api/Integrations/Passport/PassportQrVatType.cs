using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — tipo de cálculo para vat.vat_type Y para inc.inc_type: WALLET,
// FIXED, PERCENTAGE. Confirmado en XPAY-297/298 que ambos campos documentan
// EXACTAMENTE el mismo conjunto de valores — se reutiliza un único enum para
// los dos (no es una "trampa de similitud visual": es el mismo conjunto de
// valores confirmado por evidencia directa para ambos campos, no una
// suposición). Mismo patrón de serialización y guard que los demás enums Qr.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PassportQrVatType
{
    WALLET,
    FIXED,
    PERCENTAGE,
}
