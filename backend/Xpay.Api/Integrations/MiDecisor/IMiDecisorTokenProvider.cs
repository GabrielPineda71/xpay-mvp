namespace Xpay.Api.Integrations.MiDecisor;

// M2.1 — proveedor de access token OAuth2 para MiDecisor.
//
// Responsabilidad única: entregar un access token vigente, obteniéndolo del
// endpoint de token (POST /spla/oauth2/v1/token) y cacheándolo EN MEMORIA
// por proceso hasta poco antes de su expiración.
//
// NO expone refresh, retry, invalidación explícita ni consulta. La
// invalidación explícita del token se difiere a M2.2, cuando exista un
// consumidor real que defina de forma segura la semántica ante rechazo.
public interface IMiDecisorTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
