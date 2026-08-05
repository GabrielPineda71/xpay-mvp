namespace Xpay.Api.Exceptions;

// Se lanza desde AppLockHelper.ValidarResultado cuando sp_getapplock no logra
// adquirir el lock — timeout (-1), cancelación (-2) o víctima de deadlock
// (-3), al llamarse desde WalletCajaComercioService.AbrirAsync o desde
// WalletCierreDiarioComercioService.GenerarCierreAsync (Fase 70.4-B). No es
// un conflicto de datos de negocio (a diferencia de
// CajaDuplicadaException/CierreDuplicadoException/
// CajasOperativasPendientesException/CierreDiarioYaGeneradoException) — es
// contención transitoria de otra operación concurrente sobre el mismo
// comercio+fecha; reintentar más tarde puede tener éxito. Hereda de
// InvalidOperationException por consistencia con el resto de excepciones de
// dominio del proyecto.
public class OperacionCajaCierreConcurrenteException(string message) : InvalidOperationException(message);
