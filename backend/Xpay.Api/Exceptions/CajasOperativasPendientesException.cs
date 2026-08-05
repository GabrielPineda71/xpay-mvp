namespace Xpay.Api.Exceptions;

// Se lanza desde WalletCierreDiarioComercioService.GenerarCierreAsync (Fase
// 70.4-B) cuando, bajo el mismo application lock que usa
// WalletCajaComercioService.AbrirAsync, existen cajas ABIERTA/EN_CUADRE para
// el comercio+fecha que se intenta cerrar. Hereda de InvalidOperationException
// — mismo criterio que CierreDuplicadoException/CajaDuplicadaException. No
// transporta nombres de cajeros, IDs de usuario, fondos ni efectivo — solo la
// cantidad, para no filtrar información de otras cajas/usuarios al
// ADMIN_COMERCIO que solicitó el cierre.
public class CajasOperativasPendientesException(string message, int cantidadCajasOperativas)
    : InvalidOperationException(message)
{
    public int CantidadCajasOperativas { get; } = cantidadCajasOperativas;
}
