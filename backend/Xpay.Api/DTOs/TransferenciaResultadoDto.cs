namespace Xpay.Api.DTOs;

// Fase 71.2-E-G: forma exacta de los datos persistidos/reconstruidos para
// idempotencia de POST /api/wallets/transferencia — coincide con el objeto
// "data" que el endpoint ya devuelve hoy al cliente. No agrega ni expone
// ningún campo nuevo. Usado también como forma del respuesta_data_json
// almacenado (serializado/deserializado directamente, nunca un objeto
// anónimo arbitrario).
public record TransferenciaResultadoDto(long IdTransaccion, long IdWalletOrigen, long IdWalletDestino, decimal Valor);
