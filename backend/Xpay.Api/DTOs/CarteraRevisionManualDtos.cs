using System.Text.Json.Serialization;
using Xpay.Api.Common;

namespace Xpay.Api.DTOs;

// XPAY-702 - revision manual (solo ADMIN_XPAY / SUPERUSUARIO). Sin JSON crudo
// del proveedor, sin nombre completo; documento enmascarado.
public sealed record RevisionManualItemDto(
    long      IdSolicitud,
    long      IdUsuario,
    string    DocumentoEnmascarado,
    DateTime  FechaSolicitud,
    DateTime? FechaDecision,
    string?   CodigoMotivoDecision);

public sealed record ComportamientoMesDto(string? AnioMes, string? Comportamiento);

public sealed record RevisionManualResueltaDto(
    string    DecisionManual,
    decimal?  MontoAprobadoManual,
    string    ReglaAplicada,
    string    MotivoCodigo,
    string?   Observacion,
    long      IdAdmin,
    DateTime  FechaResolucionUtc,
    string    DecisionAutomaticaOriginal,
    string    EstadoAutomaticoOriginal,
    string?   MotivoAutomaticoOriginal);

public sealed record RevisionManualDetalleDto(
    long      IdSolicitud,
    long      IdUsuario,
    string    DocumentoEnmascarado,
    string    EstadoSolicitud,
    string    DecisionCrediticia,
    string?   CodigoMotivoDecision,
    IReadOnlyList<string> MotivosAutomaticos,
    DateTime  FechaSolicitud,
    DateTime? FechaDecision,
    bool?     ConInformacion,
    int?      Score,
    string?   EstadoScore,
    string?   Viabilidad,
    string?   RatingRecaudos,
    decimal?  MontoSugerido,
    int?      AlertasCount,
    string?   TipoDocumento,
    string?   EstadoDocumentoCaptura,
    string?   RangoEdad,
    string?   ConsultaAnio,
    string?   ConsultaMes,
    IReadOnlyList<ComportamientoMesDto> Comportamiento,
    PropuestaRevisionManual Propuesta,
    RevisionManualResueltaDto? Resolucion);

// Sin monto: el cupo de una aprobacion manual lo calcula XPAY por politica.
// Cualquier campo no declarado (p. ej. "montoAprobado") hace fallar el
// binding con 400.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolverRevisionManualRequest(
    string  Decision,
    string  MotivoCodigo,
    string? Observacion);
