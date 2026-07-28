namespace Xpay.Api.Common;

// Fase 71.2-E-G: envuelve el resultado de una operación financiera protegida
// por Idempotency-Key — Replayed=true cuando el resultado se reconstruyó de
// una fila COMPLETADA existente en vez de ejecutar la operación de nuevo.
public record IdempotentOperationResult<TData>(TData Data, bool Replayed);
