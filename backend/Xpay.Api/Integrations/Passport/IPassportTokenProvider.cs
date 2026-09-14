namespace Xpay.Api.Integrations.Passport;

// XPAY-272 — proveedor de access token OAuth2 client_credentials para
// Passport (Bre-B como Servicio).
//
// Responsabilidad única: entregar un access token vigente, obteniéndolo del
// endpoint de token (POST /v1/iam/oauth/tokens) y cacheándolo EN MEMORIA
// por proceso hasta poco antes de su expiración (86400s confirmados,
// sin refresh_token — al expirar se vuelve a autenticar con
// client_credentials).
//
// NO expone refresh, retry, ni invalidación explícita. Ningún consumidor
// real está conectado todavía (ver PassportHttpClient, también base-only).
public interface IPassportTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
