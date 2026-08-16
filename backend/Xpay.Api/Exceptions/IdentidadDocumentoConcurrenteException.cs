namespace Xpay.Api.Exceptions;

// Se lanza al interpretar el resultado de AppLockHelper.AdquirirAsync para la
// clave XPAY:IDENTIDAD_DOCUMENTO:{idUnidadNegocio}:{documentoNormalizado}
// (Commit 4 — consolidación de identidad verificada Veriff en Persona) —
// timeout (-1), cancelación (-2) o víctima de deadlock (-3). No reutiliza
// OperacionCajaCierreConcurrenteException porque esa excepción y su mensaje
// pertenecen semánticamente al dominio Caja/Cierre (Fase 70.4-B), no al de
// identidad. No es un conflicto de datos de negocio ni una regla KYC — es
// contención transitoria de otra verificación de identidad concurrente sobre
// el mismo documento; reintentar más tarde puede tener éxito. Hereda de
// Exception plano (no de InvalidOperationException) para no mezclarse con el
// manejo de reglas de negocio de KYC — mismo criterio que
// TransientDatabaseException/IdempotencyUnavailableException. Nunca
// transporta el número de documento ni la clave completa del lock.
public class IdentidadDocumentoConcurrenteException(string message) : Exception(message);
