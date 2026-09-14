using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// Body del endpoint de token OAuth2 de Passport (POST /v1/iam/oauth/tokens,
// contrato confirmado). A diferencia de MiDecisor, aquí client_id y
// client_secret van en el BODY JSON (no en headers), y no hay
// username/password.
//
// grant_type es SIEMPRE "client_credentials" — no es un valor que el caller
// deba (ni pueda, salvo `with`/`init` explícito) elegir, para evitar enviar
// por error un grant_type distinto al contractualmente soportado.
//
// Los nombres JSON se fijan EXPLÍCITAMENTE con [JsonPropertyName]: el
// cliente serializa con sus propias opciones y NO depende de ninguna
// naming policy implícita.
//
// Nunca se loguea una instancia de este record (contiene client_secret).
public sealed record PassportOAuthTokenRequest(
    [property: JsonPropertyName("client_id")]     string ClientId,
    [property: JsonPropertyName("client_secret")] string ClientSecret)
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; init; } = "client_credentials";
}
