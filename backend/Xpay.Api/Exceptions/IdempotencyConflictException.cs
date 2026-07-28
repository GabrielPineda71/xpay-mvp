namespace Xpay.Api.Exceptions;

// Fase 71.2-E-G: la misma Idempotency-Key ya se usó para una operación con
// un payload distinto (request_hash no coincide) — no se reproduce la
// respuesta cacheada ni se ejecuta la operación. No hereda de
// InvalidOperationException para no colisionar con el catch genérico de
// reglas de negocio (400) — se mapea explícitamente a 409 en el controller.
public class IdempotencyConflictException(string message) : Exception(message);
