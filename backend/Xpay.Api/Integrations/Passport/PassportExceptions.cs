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
//
// XPAY-358 — StatusCode/SafeErrorCode/SafeErrorMessage son diagnóstico
// ESTRUCTURADO y OPCIONAL, poblado únicamente para el caso de respuesta
// HTTP no exitosa (ver PassportHttpClient + PassportErrorBodySanitizer).
// Los constructores de timeout/fallo de conexión (un solo argumento) los
// dejan en null — no aplica diagnóstico HTTP a un fallo que nunca obtuvo
// respuesta. NINGUNA de estas tres propiedades puede contener jamás: el
// body crudo de la respuesta, headers, Authorization/Bearer/tokens,
// credenciales, ni ningún identificador crudo — SafeErrorCode/
// SafeErrorMessage ya llegan sanitizados por PassportErrorBodySanitizer
// antes de construir esta excepción; esta clase no sanitiza nada por sí
// misma, sólo transporta lo que ya fue validado. Message/ToString() NO se
// sobreescriben para incluir estos campos — permanecen con el texto
// genérico de siempre, por lo que esta excepción es segura incluso si se
// registra completa por accidente.
public sealed class PassportTransportException : PassportException
{
    public int?    StatusCode       { get; }
    public string? SafeErrorCode    { get; }
    public string? SafeErrorMessage { get; }

    public PassportTransportException(string message) : base(message)
    {
    }

    public PassportTransportException(
        string message, int? statusCode, string? safeErrorCode, string? safeErrorMessage)
        : base(message)
    {
        StatusCode       = statusCode;
        SafeErrorCode    = safeErrorCode;
        SafeErrorMessage = safeErrorMessage;
    }
}

// Respuesta recibida pero no interpretable: JSON inválido, sin
// access_token, o expires_in ausente / no numérico / <= 0.
public sealed class PassportProtocolException(string message) : PassportException(message);

// Passport respondió 401 o 403.
public sealed class PassportAuthenticationException(string message) : PassportException(message);
