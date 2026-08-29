namespace Xpay.Api.Exceptions;

// Se lanza al interpretar el resultado de AppLockHelper.AdquirirAsync para la
// clave XPAY:KYC_USUARIO:{idUsuario} (Commit 5 — serialización del ciclo de
// vida de sesiones KYC de un mismo usuario: creación de sesión Veriff y
// procesamiento de decisión, para que EsActual/Usuario.EstadoKycActual nunca
// queden en una condición de carrera) — timeout (-1), cancelación (-2) o
// víctima de deadlock (-3). No reutiliza IdentidadDocumentoConcurrenteException
// porque esa excepción y su mensaje pertenecen semánticamente al lock de
// unicidad de documento entre Personas, no a la serialización del ciclo de
// sesiones KYC de un usuario — son recursos distintos, con locks distintos.
// Tampoco reutiliza OperacionCajaCierreConcurrenteException (dominio
// Caja/Cierre, Fase 70.4-B). No es un conflicto de datos de negocio ni una
// regla KYC — es contención transitoria de otra operación de sincronización
// KYC concurrente sobre el mismo usuario; reintentar más tarde puede tener
// éxito. Hereda de Exception plano (no de InvalidOperationException) para no
// mezclarse con el manejo de reglas de negocio de KYC — mismo criterio que
// TransientDatabaseException/IdempotencyUnavailableException/
// IdentidadDocumentoConcurrenteException. Nunca transporta idUsuario,
// idKycVerificacion, sessionId ni la clave completa del lock.
public class KycUsuarioConcurrenteException(string message) : Exception(message);
