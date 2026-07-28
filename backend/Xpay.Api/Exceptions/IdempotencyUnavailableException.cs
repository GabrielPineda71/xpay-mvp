namespace Xpay.Api.Exceptions;

// Fase 71.2-E-G: tras una violación UNIQUE de Idempotency-Key, no fue
// posible leer un resultado COMPLETADA dentro de los reintentos acotados de
// IdempotencyStore.ResolverReplayAsync (condición transitoria de
// infraestructura, no un conflicto real de payload) — se mapea a 503.
public class IdempotencyUnavailableException(string message) : Exception(message);
