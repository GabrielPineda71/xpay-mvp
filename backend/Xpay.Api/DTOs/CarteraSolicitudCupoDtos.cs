namespace Xpay.Api.DTOs;

// ── Cartera Ordinaria — Originación de cupo (ETAPA 2: contratos) ────────
// Sólo estructura. Sin endpoint, sin controller action, sin servicio, sin
// evaluación crediticia, sin proveedor, sin persistencia. Usuario/persona
// se derivarán del contexto autenticado (no del body) en una etapa
// posterior; esa derivación NO se implementa aquí.

// XPAY-676B — solicitar-cupo NO tiene body: el cliente solicita ser evaluado
// para obtener un cupo, no elige ni solicita un monto. XPAY determina el cupo
// con CarteraDecisionEngine + CarteraPolicyParameters.Vigente(). La
// Idempotency-Key llega por header HTTP `Idempotency-Key` (GUID).

// Proyección expuesta al cliente para una solicitud de cupo. NO expone
// score observado ni su threshold, edad, snapshot interno de política,
// viabilidad, rating de recaudos, monto sugerido del proveedor, payload
// del proveedor, correlación interna ni detalles técnicos del intento.
public record SolicitudCupoResponse(
    long      IdSolicitud,
    string    EstadoSolicitud,
    string    DecisionCrediticia,
    decimal?  MontoAprobado,
    string?   CodigoMotivoDecision,
    DateTime  FechaSolicitud,
    DateTime? FechaDecision,
    long?     IdCupoOrdinario);
