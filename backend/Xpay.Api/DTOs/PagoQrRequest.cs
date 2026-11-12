namespace Xpay.Api.DTOs;

// Fase 71.2-E-D: IdWalletUsuario y CreadoPor se retiraron del contrato — la
// wallet pagadora y el autor se resuelven exclusivamente en el controller
// desde los claims idPersona/idUsuario del JWT (QrController.Pagar), mismo
// principio ya aplicado a TransferenciaWalletRequest en 71.2-E-C. Ver
// docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md.
public class PagoQrRequest : IConPinTransaccional
{
    public string CodigoQr { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string? Descripcion { get; set; }
    // PIN transaccional: lo verifica y borra RequierePinTransaccional antes de la action.
    public string? Pin { get; set; }
}
