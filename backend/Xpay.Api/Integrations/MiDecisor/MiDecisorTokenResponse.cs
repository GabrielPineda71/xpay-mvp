using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.MiDecisor;

// Proyección MÍNIMA de la respuesta del endpoint de token OAuth2 de MiDecisor
// — sólo lo que M2.1 necesita para cachear el access token.
//
// El contrato documenta además token_type / issued_at / refresh_token; M2.1
// NO los usa y por tanto NO los modela (refresh_token queda explícitamente
// fuera de alcance).
//
// TRANSPORTE DEFENSIVO — ambos campos nullable: una respuesta sin
// access_token o sin expires_in es un error de PROTOCOLO (no una NRE).
//
// `expires_in` es STRING en el contrato: se conserva tal cual y el provider
// lo parsea defensivamente a entero de segundos.
public sealed class MiDecisorTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public string? ExpiresIn { get; set; }
}
