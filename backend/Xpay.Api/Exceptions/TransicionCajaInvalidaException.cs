namespace Xpay.Api.Exceptions;

// Hereda de InvalidOperationException — mismo criterio que CierreDuplicadoException.
// Representa cualquier transición de estado incompatible con la operación
// solicitada (diseño Fase 70.4, sección 11): recargar sin caja ABIERTA;
// iniciar-cuadre/cerrar en un estado incorrecto; revisar fuera de
// CON_DIFERENCIA/CERRADA_AUTOMATICAMENTE; abrir después de la hora límite;
// abrir en una sede inactiva; corregir el fondo inicial con recargas ya
// vinculadas o con la caja fuera de ABIERTA. Un mismo tipo para todas estas
// causas — el mensaje distingue el caso concreto, no la clase.
public class TransicionCajaInvalidaException(string message) : InvalidOperationException(message);
