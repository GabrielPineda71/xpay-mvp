namespace Xpay.Api.Exceptions;

// Fase 71.2-E-E: hereda de InvalidOperationException — mismo criterio que
// TransicionCajaInvalidaException (Fase 70.4). Representa específicamente el
// intento de confirmar o rechazar un retiro que ya no está en estado
// PENDIENTE (ya PAGADO, ya RECHAZADO, o cualquier otro estado). Se distingue
// del resto de InvalidOperationException del mismo método (que sí son 400 —
// datos faltantes o inválidos) porque esta es una operación bien formada que
// entra en conflicto con el estado actual del recurso, no una solicitud
// inválida — el controller la mapea a 409, no a 400.
public class TransicionRetiroInvalidaException(string message) : InvalidOperationException(message);
