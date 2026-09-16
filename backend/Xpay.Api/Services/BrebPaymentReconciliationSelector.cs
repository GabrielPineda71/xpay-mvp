using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-381 — predicado PURO (sin I/O, sin EF) que decide qué retiros son
// candidatos a reconciliación automática. Extraído a su propia función
// exactamente por el mismo motivo que WalletReservationCalculator/
// BrebPaymentStateMachine/BrebKeyResolutionRequestBuilder: ningún test de
// este repositorio usa una base de datos real, así que la lógica de
// selección debe poder probarse contra objetos en memoria.
//
// Debe mantenerse EN SINTONÍA con el filtro EF de
// BrebPaymentReconciliationService.ObtenerCandidatosAsync (mismas dos
// condiciones); ese filtro EF existe además de este predicado (aplicado en
// memoria tras materializar) como capa defensiva doble, no como sustituto.
public static class BrebPaymentReconciliationSelector
{
    public const string EstadoCandidato = "ENVIADO_PASSPORT";

    // Único universo reconciliable: el retiro ya fue enviado a Passport
    // (nunca CREADO/PENDIENTE_ENVIO_PASSPORT — eso no le corresponde a este
    // reconciliador) Y ya tiene el payment_id que Passport devolvió (nunca
    // null/vacío — sin payment_id no hay nada que consultar). Nunca acepta
    // un payment_id que no sea el ya persistido por XPAY mismo — este
    // predicado ni siquiera conoce el concepto de "payment_id externo".
    public static bool EsCandidato(PassportBrebRetiro retiro) =>
        retiro.Estado == EstadoCandidato && !string.IsNullOrWhiteSpace(retiro.PassportPaymentId);
}
