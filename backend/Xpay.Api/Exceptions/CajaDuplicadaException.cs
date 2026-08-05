namespace Xpay.Api.Exceptions;

// Hereda de InvalidOperationException — mismo criterio que CierreDuplicadoException
// (Fase 70.3): un catch genérico existente la sigue tratando como 400 por defecto;
// el controller de cajas la intercepta explícitamente antes para devolver 409.
// Representa la violación de UNIQUE(id_usuario_cajero, id_comercio, fecha_operativa)
// al intentar abrir una segunda caja el mismo día — incluida la carrera de
// concurrencia entre dos aperturas simultáneas (la segunda transacción en llegar
// falla por el índice único, no por una condición de carrera silenciosa).
public class CajaDuplicadaException(string message) : InvalidOperationException(message);
