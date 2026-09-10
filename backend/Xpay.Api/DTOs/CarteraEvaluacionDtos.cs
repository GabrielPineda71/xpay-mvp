namespace Xpay.Api.DTOs;

// ── M2.4d — contratos de disparo / estado de la evaluación crediticia ──────
// SÓLO proyección segura para el usuario final. NUNCA exponen: payload del
// proveedor, P0 raw, score crudo, resultado_tecnico interno del intento,
// senal_posible_suplantacion, correlación interna, edad, viabilidad, rating,
// monto sugerido del proveedor ni snapshot de política.

// POST /api/cartera-ordinaria/solicitudes/{idSolicitud}/iniciar-evaluacion
// El body no lleva datos: la solicitud ya existe y el monto está fijado en la
// originación. La Idempotency-Key va por header HTTP (misma convención que
// solicitar-cupo); no se persiste por separado — la idempotencia real la da la
// transición durable RECIBIDA→CONSULTANDO_RIESGO del servicio de consulta.
public record IniciarEvaluacionResponse(
    long      IdSolicitud,
    string    EstadoSolicitud,
    string    DecisionCrediticia,
    decimal?  MontoAprobado,
    DateTime? FechaDecision,
    bool      RequiereRevisionManual,
    string    Mensaje);

// GET /api/cartera-ordinaria/solicitudes/{idSolicitud}
public record SolicitudEvaluacionEstadoResponse(
    long      IdSolicitud,
    string    EstadoSolicitud,
    string    DecisionCrediticia,
    decimal?  MontoAprobado,
    DateTime? FechaDecision,
    bool      RequiereRevisionManual);

// POST /api/cartera-ordinaria/admin/solicitudes/{idSolicitud}/reconciliar-consulta-atascada
// motivoOperativo: texto corto NO sensible (no PII, no credenciales). Se audita
// saneado vía AuditLogService; no se persiste en columna dedicada (ver XPAY-204
// — gap de evidencia durable específica).
public record ReconciliarConsultaAtascadaRequest(
    string MotivoOperativo);

public record ReconciliarConsultaAtascadaResponse(
    long   IdSolicitud,
    string EstadoSolicitud,
    string Resultado);
