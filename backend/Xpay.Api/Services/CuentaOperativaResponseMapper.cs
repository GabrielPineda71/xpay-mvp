using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;

namespace Xpay.Api.Services;

// XPAY-372 — mapea la PassportAccountResponse (Retrieve Account) ya
// deserializada al DTO sanitizado que verá el admin. Función PURA — no hace
// I/O, no llama a Passport — 100% testeable offline. Mismo criterio de
// extracción que BrebKeyResolutionResponseMapper (XPAY-371).
//
// account_id NUNCA se expone en claro — sólo su fingerprint (Xpay.Api.
// Common.Fingerprint), mismo criterio ya usado para key_id/customer_id en
// el harness de certificación. customer_id de la respuesta tampoco se
// expone: no aporta nada al admin que necesite ver el estado de LA cuenta
// operativa (el admin ya conoce, por diseño, que la cuenta pertenece a
// XPAY — no hace falta mostrarle un UUID interno de Passport).
public static class CuentaOperativaResponseMapper
{
    public static CuentaOperativaResponse ToSanitizedResponse(
        string accountId, PassportAccountResponse response, DateTime consultadoEnUtc)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new CuentaOperativaResponse
        {
            AccountIdFingerprint = Fingerprint.Compute(accountId),
            Estado               = response.Status,
            // available_balance/pending_balance pueden venir con currency
            // distinta entre sí en teoría (el contrato no lo prohíbe
            // explícitamente) — XPAY-372 no asume que coincidan; expone la
            // de available_balance como "la" currency de la cuenta (la
            // única con significado operativo hoy — no hay todavía ningún
            // consumidor de pending_balance.currency) y dejar esa
            // ambigüedad documentada en vez de resolverla por inferencia.
            Currency             = response.AvailableBalance?.Currency,
            SaldoDisponible      = response.AvailableBalance?.Value,
            SaldoPendiente       = response.PendingBalance?.Value,
            ConsultadoEnUtc      = consultadoEnUtc,
        };
    }
}
