using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-279 — respuesta de Link Account (POST /v1/accounts/link) y de
// Retrieve Account (GET /v1/accounts/:account_id) — misma forma documentada
// para ambos endpoints (ver XPAY-278).
//
// `Id` es el account_id de Passport. `CustomerId` establece la relación
// documentada account→customer (1:N confirmado: un customer puede tener
// múltiples accounts).
//
// TRANSPORTE DEFENSIVO — todas las propiedades nullable, mismo criterio que
// PassportCustomerResponse: campos adicionales desconocidos no rompen la
// deserialización; campos documentados ausentes quedan en null.
public sealed class PassportAccountResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("account_type")]
    public string? AccountType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("available_balance")]
    public PassportBalance? AvailableBalance { get; set; }

    [JsonPropertyName("pending_balance")]
    public PassportBalance? PendingBalance { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

// Objeto de balance ({value, currency}) documentado para available_balance/
// pending_balance. `Value` se modela como decimal? por consistencia con el
// resto de XPAY (ver p.ej. PassportBrebRetiro.Valor, LedgerMovimiento.Valor).
//
// XPAY-281 (hallazgo XPAY-280 #1): el contrato oficial confirma los nombres
// de campo pero NO confirma explícitamente si `value` viaja como número JSON
// (150000) o como string numérico ("150000") — ambigüedad puramente de
// REPRESENTACIÓN, no de dominio (el valor de negocio sigue siendo decimal).
// [JsonNumberHandling(AllowReadingFromString)] resuelve esa ambigüedad con
// la capacidad estándar de System.Text.Json, aplicada LOCALMENTE a esta
// propiedad (no globalmente a la integración): acepta 150000 y "150000"/
// "150000.25" produciendo el mismo decimal en ambos casos, y sigue
// rechazando (JsonException, deserialización fallida) cualquier string no
// numérico como "not-a-number" — nunca convierte un valor inválido en
// null/0 de forma silenciosa. Verificado empíricamente (ver XPAY-281).
public sealed class PassportBalance
{
    [JsonPropertyName("value")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Value { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}
