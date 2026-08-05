namespace Xpay.Api.Exceptions;

// Se lanza desde WalletCajaComercioService.AbrirAsync (Fase 70.4-B) cuando ya
// existe un cierre diario (cualquier estado) para el comercio+fecha que se
// intenta abrir. Hereda de InvalidOperationException — mismo criterio que el
// resto de excepciones de dominio del proyecto. Semánticamente distinta de
// CajaDuplicadaException (esa es sobre otra caja, no sobre un cierre) y de
// CajasOperativasPendientesException (dirección opuesta: cajas bloqueando un
// cierre, no un cierre bloqueando una caja) — no se reutiliza ninguna de las
// dos.
public class CierreDiarioYaGeneradoException(string message) : InvalidOperationException(message);
