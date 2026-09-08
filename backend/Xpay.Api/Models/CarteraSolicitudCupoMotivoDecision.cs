namespace Xpay.Api.Models;

// M2.4b — fila de la lista ORDENADA de motivos de la decisión crediticia de
// una solicitud (tabla dbo.cartera_solicitud_cupo_motivos_decision,
// migración 040). Contrato mínimo cerrado en XPAY-182:
//   - `Orden` 1-based, sin huecos ; `Orden == 1` es el motivo primario y se
//     replica en cartera_solicitudes_cupo.codigo_motivo_decision.
//   - `CodigoMotivo` ∈ CarteraMotivoDecision (vocabulario controlado en código).
//   - Sin es_primario, sin fecha_registro, sin texto libre.
// APROBADA ⇒ 0 filas. RECHAZADA / NO_DECIDIBLE ⇒ ≥ 1 fila.
public class CarteraSolicitudCupoMotivoDecision
{
    public long   IdMotivo     { get; set; }
    public long   IdSolicitud  { get; set; }
    public short  Orden        { get; set; }
    public string CodigoMotivo { get; set; } = string.Empty;
}
