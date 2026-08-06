namespace Xpay.Api.Exceptions;

// Se lanza desde WalletRecargaComercioService.RecargarWalletAsync (Fase
// 70.4-C) cuando el usuario que intenta recargar no tiene una caja propia
// ABIERTA para su id_usuario_cajero + id_comercio + id_establecimiento +
// fecha_operativa Colombia. También cubre el caso en que la caja
// desapareció entre el chequeo amigable y la relectura bajo lock. Hereda de
// InvalidOperationException — mismo criterio que el resto de excepciones de
// dominio del proyecto. No transporta ningún dato de otras cajas (ajenas o
// de otro usuario/comercio/sede) — el mensaje es genérico a propósito, para
// no revelar si existe o no una caja fuera del alcance del solicitante.
public class CajaNoAbiertaException(string message) : InvalidOperationException(message);
