namespace Xpay.Api.Exceptions;

// Hereda de InvalidOperationException para que cualquier catch genérico
// existente la siga tratando como 400 por defecto; los controllers de
// Fase 70.3 la interceptan explícitamente antes para devolver 409.
public class CierreDuplicadoException(string message) : InvalidOperationException(message);
