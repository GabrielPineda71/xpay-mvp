using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — canal para Create QR Code, contrato confirmado en XPAY-297:
// IM, POS, APP, ECOMM, MPOS, CB, OFC, ATM. Mismo patrón de serialización y
// guard (Enum.IsDefined en PassportQrClient) que PassportQrType/PassportKeyType.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PassportQrChannel
{
    IM,
    POS,
    APP,
    ECOMM,
    MPOS,
    CB,
    OFC,
    ATM,
}
