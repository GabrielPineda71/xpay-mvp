namespace Xpay.Api.Models;

// Evidencia durable e INMUTABLE de la autorización del titular para consulta en
// centrales de riesgo. Una fila = un evento de aceptación. Append-only: no hay
// updated_at, no hay estado/revocación, no se edita ni se reasigna. Una nueva
// aceptación es una fila nueva. Migración 041.
public class CarteraAutorizacionConsultaRiesgo
{
    public long     IdAutorizacion      { get; set; }
    // Titular de la información (sujeto de la autorización).
    public long     IdPersona           { get; set; }
    // Cuenta autenticada que ejecutó "AUTORIZO" (actor de la aceptación).
    public long     IdUsuario           { get; set; }
    // Solicitud durante la cual se capturó la aceptación. En V1 (XPAY-197) es
    // además el RELATION_ORIGIN_ID: la aceptación autoriza EXCLUSIVAMENTE la
    // consulta de esta misma solicitud (sin reutilización cross-request).
    public long     IdSolicitudOrigen   { get; set; }
    // Identificador estable de la versión del texto aceptado
    // (CarteraAutorizacionConsultaRiesgoTextos.V1_Version).
    public string   VersionTexto        { get; set; } = string.Empty;
    // SHA-256 hex del texto exacto aceptado.
    public string   HashTexto           { get; set; } = string.Empty;
    // Copia literal del texto mostrado y aceptado (evidencia autocontenida).
    public string   TextoSnapshot       { get; set; } = string.Empty;
    // Instante de aceptación (UTC). NO es el inicio del reloj de retención de
    // 5 años (ese arranca en RELATION_END = cancelación del cupo, fase futura).
    public DateTime FechaAceptacionUtc  { get; set; }
    // Traza técnica opcional del request de captura.
    public string?  CorrelationId       { get; set; }
}
