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
