namespace Xpay.Api.Exceptions;

// Fase USUARIOS-ADMIN-3: mismo criterio que TransicionRetiroInvalidaException
// / TransicionCajaInvalidaException — hereda de InvalidOperationException.
// Representa específicamente el intento de activar/inactivar/desbloquear un
// usuario cuyo estado actual no permite esa transición (ej. desbloquear un
// usuario que no está BLOQUEADO). Se distingue del resto de
// InvalidOperationException del mismo método (auto-acción, último
// administrador — esos sí son 400, solicitudes inválidas por regla de
// negocio) porque esta es una operación bien formada que entra en conflicto
// con el estado actual del recurso — el controller la mapea a 409, no a 400.
public class TransicionUsuarioInvalidaException(string message) : InvalidOperationException(message);
