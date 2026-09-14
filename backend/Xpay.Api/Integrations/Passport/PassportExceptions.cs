namespace Xpay.Api.Integrations.Passport;

// Jerarquía mínima de errores de dominio para la integración Passport.
// Mismo estilo que backend/Xpay.Api/Exceptions/* y
// Integrations/MiDecisor/MiDecisorExceptions.cs (constructor primario,
// mensajes cortos ya saneados) — implementación propia, independiente.
//
// REGLA DE SANEAMIENTO — ningún mensaje de estas excepciones puede contener:
// ClientId, ClientSecret, access_token, Secret Token de webhook, el body de
// la petición o de la respuesta, ni PII. Sólo categoría técnica + (a lo
// sumo) código de estado HTTP.
//
// XPAY-272 no reintenta: cada intento de refresh/llamada produce como
// máximo 1 llamada HTTP y, ante fallo, lanza la excepción correspondiente
// sin reintentar.

// Base de todos los fallos de la integración Passport.
public class PassportException(string message) : Exception(message);

// Falta configuración obligatoria (base URL o alguna credencial), o la
// BaseUrl configurada no es una URL http/https absoluta válida. Se lanza
// ANTES de cualquier llamada HTTP.
public sealed class PassportConfigurationException(string message) : PassportException(message);

// Fallo de transporte: HttpRequestException, timeout no provocado por el
// caller, o cualquier respuesta non-2xx que no sea 401/403.
public sealed class PassportTransportException(string message) : PassportException(message);

// Respuesta recibida pero no interpretable: JSON inválido, sin
// access_token, o expires_in ausente / no numérico / <= 0.
public sealed class PassportProtocolException(string message) : PassportException(message);

// Passport respondió 401 o 403.
public sealed class PassportAuthenticationException(string message) : PassportException(message);
