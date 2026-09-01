namespace Xpay.Api.Integrations.MiDecisor;

// Jerarquía mínima de errores de dominio para la integración MiDecisor.
// Mismo estilo que backend/Xpay.Api/Exceptions/* (constructor primario,
// mensajes cortos ya saneados).
//
// REGLA DE SANEAMIENTO — ningún mensaje de estas excepciones puede contener:
// Client_id, Client_secret, username, password, access_token, el body de la
// petición o de la respuesta, ni PII. Sólo categoría técnica + (a lo sumo)
// código de estado HTTP.
//
// M2.1 NO reintenta: cada intento de refresh produce como máximo 1 llamada
// HTTP y, ante fallo, lanza la excepción correspondiente sin reintentar.

// Base de todos los fallos de la integración MiDecisor.
public class MiDecisorException(string message) : Exception(message);

// Falta configuración obligatoria (base URL o alguna credencial). Se lanza
// ANTES de cualquier llamada HTTP.
public sealed class MiDecisorConfigurationException(string message) : MiDecisorException(message);

// Fallo de transporte: HttpRequestException, timeout no provocado por el
// caller, o cualquier respuesta non-2xx del endpoint de auth que no sea
// 401/403.
public sealed class MiDecisorTransportException(string message) : MiDecisorException(message);

// Respuesta recibida pero no interpretable: JSON inválido, sin access_token,
// o expires_in ausente / no numérico / <= 0.
public sealed class MiDecisorProtocolException(string message) : MiDecisorException(message);

// El endpoint de auth respondió 401 o 403.
public sealed class MiDecisorAuthenticationException(string message) : MiDecisorException(message);
