using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-305 — body de POST /v1/qrcodes/decode (contrato confirmado en
// XPAY-304 vía docs.passportfintech.com/EN/decode-qr-code). Shape totalmente
// distinto al de Create QR Code (PassportCreateQrCodeRequest): sólo dos
// campos, ninguno reutilizado de ese DTO (XPAY-304 FASE 17/18).
//
// customer_id: String, requerido. La documentación NO exige explícitamente
// formato UUID — no se inventa esa validación (XPAY-304/305).
// qr_code_data: String, requerido — payload EMVCo-compliant. Se transporta
// verbatim: no se parsea, no se normaliza, no se valida longitud ni un
// regex EMVCo no documentado.
//
// Nunca se loguea una instancia de este record (contiene un payload QR y un
// identificador de cliente).
public sealed record PassportDecodeQrCodeRequest(
    [property: JsonPropertyName("customer_id")]  string CustomerId,
    [property: JsonPropertyName("qr_code_data")] string QrCodeData);
