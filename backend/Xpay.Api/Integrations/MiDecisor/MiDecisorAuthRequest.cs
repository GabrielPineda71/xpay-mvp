using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.MiDecisor;

// Body del endpoint de token OAuth2 de MiDecisor (POST /spla/oauth2/v1/token,
// contrato confirmado). SÓLO los dos campos documentados del body.
//
// Client_id / Client_secret NO van aquí: son HEADERS de la petición, no body.
//
// Los nombres JSON se fijan EXPLÍCITAMENTE con [JsonPropertyName]: el
// provider serializa con sus propias opciones y NO depende de ninguna
// naming policy implícita.
//
// Nunca se loguea una instancia de este record (contiene credenciales).
public sealed record MiDecisorAuthRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);
