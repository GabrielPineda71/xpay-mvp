namespace Xpay.Api.DTOs;

// Fase 71.2-E-G: forma exacta de los datos persistidos/reconstruidos para
// idempotencia de POST /api/qr/pagar — coincide con el objeto "data" que el
// endpoint ya devuelve hoy al cliente. No agrega ni expone ningún campo
// nuevo.
public record PagoQrResultadoDto(long IdVentaQr, long IdTransaccion, long IdComercio, long IdTienda, long IdWalletUsuario, decimal Valor, string Estado);
