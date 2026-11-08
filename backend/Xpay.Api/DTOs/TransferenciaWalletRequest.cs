namespace Xpay.Api.DTOs;

// Fase 71.2-E-C: IdWalletOrigen y CreadoPor se retiraron del contrato — la
// wallet origen y el autor se resuelven exclusivamente en el controller desde
// los claims idPersona/idUsuario del JWT (WalletsController.Transferir), nunca
// desde el cliente. Ver docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md.
public class TransferenciaWalletRequest
{
    public long IdWalletDestino { get; set; }
    public decimal Valor { get; set; }
    public string? Descripcion { get; set; }
    // Usuario que el ordenante confirmó con GET /api/wallets/destinatario.
    // Obligatorio: la transferencia se rechaza si falta o si ya no es el
    // titular de la wallet destino (ver DestinatarioWalletService).
    public string? UsuarioDestinoEsperado { get; set; }
}
