using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// Proyección de la respuesta del endpoint de token OAuth2 de Passport
// (contrato confirmado): expires_in, access_token, token_id, token_type,
// scopes, account_id, created_at, roles.
//
// El PassportTokenProvider (XPAY-272) sólo CONSUME AccessToken, ExpiresIn y
// TokenType para el cache; el resto de campos se modelan aquí para que
// existan y estén disponibles a futuros consumidores (Fase posterior), sin
// que su formato exacto de serialización esté validado todavía end-to-end
// contra Passport real — se leen de forma defensiva (nullable, tipos
// simples/arreglos de string) para no romper la deserialización si el
// formato observado difiere ligeramente del asumido aquí.
//
// `expires_in` se modela como número entero (JSON number, no string) —
// consistente con RFC 6749 y con el valor de ejemplo confirmado (86400),
// sin evidencia contractual de que Passport lo envíe como string (a
// diferencia de MiDecisor, donde sí está documentado como string).
//
// TRANSPORTE DEFENSIVO — AccessToken/ExpiresIn nullable: una respuesta sin
// alguno de los dos es un error de PROTOCOLO (no una NRE).
//
// Nunca se loguea una instancia de este tipo (contiene access_token).
public sealed class PassportOAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("token_id")]
    public string? TokenId { get; set; }

    [JsonPropertyName("scopes")]
    public string[]? Scopes { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    // RAW string — formato de fecha no confirmado contractualmente; se
    // conserva verbatim en vez de parsear a DateTime (mismo criterio que los
    // campos "Raw" de MiDecisorResultado).
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("roles")]
    public string[]? Roles { get; set; }
}
