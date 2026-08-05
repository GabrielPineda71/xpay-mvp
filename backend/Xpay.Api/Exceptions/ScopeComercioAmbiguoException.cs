namespace Xpay.Api.Exceptions;

// Hereda de InvalidOperationException — mismo criterio que CierreDuplicadoException.
// El usuario tiene más de un scope COMERCIO activo (más de una fila ACTIVO en
// comercio_usuarios) y el sistema no puede determinar automáticamente sobre
// qué comercio actuar (diseño Fase 70.4, secciones 4 y 11). No es un problema
// de autorización — el usuario sí tiene acceso, de hecho a más de un
// contexto — por eso se mapea a 409, no a 403. Bloquea toda la superficie de
// api/comercio/cajas/*, incluidas las lecturas de conveniencia, hasta que
// exista una subfase de selección explícita de contexto/sesión (sección 21).
public class ScopeComercioAmbiguoException(string message) : InvalidOperationException(message);
