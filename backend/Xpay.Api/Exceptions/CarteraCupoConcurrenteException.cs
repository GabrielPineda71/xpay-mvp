namespace Xpay.Api.Exceptions;

// Se lanza al interpretar el resultado de AppLockHelper.AdquirirAsync para la
// clave XPAY:CARTERA_CUPO:{idUsuario} (compartida por
// CarteraOrdinariaService.AsignarCupoAsync y
// CarteraMaterializacionCupoStore.MaterializarCupoAsync / M2.4c — serialización
// de la asignación admin del cupo ordinario con la materialización TX2 y con
// otra asignación admin del mismo usuario) — timeout (-1), cancelación (-2) o
// víctima de deadlock (-3). No es un conflicto de datos de negocio ni una regla
// de cupo — es contención transitoria de otra operación concurrente sobre el
// cupo del mismo usuario; reintentar más tarde puede tener éxito. Hereda de
// Exception plano (no de InvalidOperationException) para no mezclarse con el
// manejo de reglas de negocio del controller — mismo criterio que
// KycUsuarioConcurrenteException / IdentidadDocumentoConcurrenteException. Nunca
// transporta idUsuario ni la clave completa del lock.
public class CarteraCupoConcurrenteException(string message) : Exception(message);
