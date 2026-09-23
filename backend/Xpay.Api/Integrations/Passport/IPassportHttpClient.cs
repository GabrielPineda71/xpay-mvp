namespace Xpay.Api.Integrations.Passport;

// XPAY-272 — abstracción HTTP autenticada reutilizable para las futuras
// operaciones Passport: customers/accounts, Bre-B keys, resolve-key,
// payments/breb, QR Bre-B.
//
// Esta interfaz NO implementa ninguna de esas operaciones — sólo el
// mecanismo genérico de envío autenticado (obtener token vigente, agregar
// Authorization: Bearer, resolver contra BaseUrl, (de)serializar JSON).
// Cada endpoint concreto se construirá en una fase posterior sobre esta
// abstracción, sin duplicar la lógica de autenticación/transporte.
public interface IPassportHttpClient
{
    Task<TResponse?> PostAsync<TRequest, TResponse>(
        string relativePath, TRequest body, CancellationToken cancellationToken = default);

    // XPAY-517 — método ADITIVO, exclusivo de necesidades de certificación
    // (M4-T5): además del objeto tipado ya deserializado, expone el JSON
    // crudo de una respuesta exitosa ANTES de la deserialización tipada.
    // Necesario porque PostAsync (usado por TODOS los consumidores
    // productivos) deserializa directo desde el stream HTTP
    // (ReadFromJsonAsync) sin materializar nunca el texto — y
    // System.Text.Json descarta silenciosamente cualquier propiedad JSON
    // no modelada en TResponse (p. ej. `tip`, ausente de
    // PassportDecodeQrCodeResponse; ya documentado como limitación
    // estructural en la evidencia de M4-T2).
    //
    // Implementación COMPLETAMENTE SEPARADA de SendAsync/PostAsync — no
    // comparte código con ellos ni modifica su comportamiento en absoluto.
    // NINGÚN consumidor productivo usa este método: no se invoca desde
    // Program.cs ni desde ningún flujo de producto — su único consumidor
    // es el harness de certificación (PassportQrClient.
    // DecodeQrCodeWithRawCaptureAsync). El body exitoso capturado está
    // acotado en tamaño (ver PassportHttpClient) — nunca ilimitado.
    Task<(TResponse? Response, string? RawResponseJson)> PostWithRawCaptureAsync<TRequest, TResponse>(
        string relativePath, TRequest body, CancellationToken cancellationToken = default);

    Task<TResponse?> GetAsync<TResponse>(
        string relativePath, CancellationToken cancellationToken = default);

    // XPAY-293 — PATCH sin body: los primeros consumidores confirmados
    // (Suspend/Activate Bre-B Key) no tienen request body documentado. Si un
    // futuro endpoint PATCH sí lo requiere, se agregará un overload con body
    // en su momento — no se anticipa aquí sin evidencia.
    Task<TResponse?> PatchAsync<TResponse>(
        string relativePath, CancellationToken cancellationToken = default);

    // XPAY-293 — DELETE genérico. No genérico en TResponse: el primer
    // consumidor confirmado (Delete Bre-B Key) responde 204 No Content sin
    // body, y esta operación NUNCA intenta deserializar una respuesta —
    // no es un caso particular de Bre-B Key, es la semántica correcta para
    // cualquier DELETE sin cuerpo de respuesta.
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
