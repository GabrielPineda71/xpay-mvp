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
}
