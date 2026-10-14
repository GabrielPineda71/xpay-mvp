namespace Xpay.Api.Integrations.MiDecisor;

// XPAY-717 — evidencia durable para la regla de reconsulta en ambiente
// productivo, calculada para el TITULAR DOCUMENTAL que consultaría MiDecisor
// (tipo_documento + numero_documento de la persona de la solicitud), de forma
// transversal a usuarios, personas, solicitudes y unidades de negocio.
//   UltimaRespuestaUtc  → MAX(fecha_fin) de intentos FINALIZADO cuyo
//                         resultado_tecnico prueba respuesta del proveedor
//                         (ReglaReconsultaMiDecisor.ResultadosConRespuesta);
//                         null si no hay ninguna.
//   TieneCupoQueBloquea → existe un cupo ordinario en estado ACTIVO o
//                         SUSPENDIDO de algún usuario del mismo titular.
public sealed record RiesgoProduccionSnapshot(DateTime? UltimaRespuestaUtc, bool TieneCupoQueBloquea);

// Lecturas del gate productivo. Sólo lectura. Devuelve null si la identidad
// documental de la solicitud es insuficiente (el gate falla cerrado).
public interface IRiesgoProduccionStore
{
    Task<RiesgoProduccionSnapshot?> CargarAsync(long idSolicitud, CancellationToken cancellationToken = default);
}
