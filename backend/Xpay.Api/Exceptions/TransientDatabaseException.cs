namespace Xpay.Api.Exceptions;

// Fase 71.2-E-G: deadlock (1205) o lock timeout (1222) durante una
// operación financiera — la transacción ya se revirtió antes de lanzar
// esta excepción. No es un error del cliente ni un conflicto de
// idempotencia — se mapea a 503. No se reintenta automáticamente la
// operación completa en esta primera implementación (documentado como
// mejora futura en docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md §16.11).
public class TransientDatabaseException(string message, Exception inner) : Exception(message, inner);
