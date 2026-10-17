using System.Linq.Expressions;
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

    // P0 J.1 - ÚNICA definición del umbral de revisión humana (2 h), usada por
    // el reconciliador (aviso) y por el listado admin. Sin efecto financiero.
    public static readonly TimeSpan UmbralRevisionHumana = TimeSpan.FromHours(2);

    public static DateTime CorteRevisionHumana(DateTime ahoraUtc) => ahoraUtc - UmbralRevisionHumana;

    // P0 J.1 - envío incierto que requiere revisión humana: ENVIADO_PASSPORT,
    // sin payment_id y con más de UmbralRevisionHumana desde el envío
    // (FechaSolicitud solo como respaldo defensivo de datos históricos). Una
    // sola expresión: traducible a SQL y compilable en memoria. Determinista:
    // el corte llega como argumento. Complemento de EsCandidato.
    public static Expression<Func<PassportBrebRetiro, bool>> RequiereRevisionHumana(DateTime corteUtc) =>
        r => r.Estado == EstadoCandidato
          && (r.PassportPaymentId == null || r.PassportPaymentId.Trim() == "")
          && (r.FechaEnvioPassport ?? r.FechaSolicitud) < corteUtc;
}
