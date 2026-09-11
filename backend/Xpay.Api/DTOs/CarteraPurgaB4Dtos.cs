namespace Xpay.Api.DTOs;

// ── XPAY-213/214 — purge B4 (política de retención, XPAY-212 §Q.8) ────────
// POST /api/cartera-ordinaria/admin/purge-b4/ejecutar-lote
// Respuesta EXCLUSIVAMENTE agregada y segura: nunca expone idSolicitud, raw,
// score, documento, comportamiento ni ningún dato del proveedor. El cutoff
// (UTC now - 5 años calendario) se deriva siempre internamente — el cliente
// no puede enviar ni cambiar duración, clock, ni saltarse el hold.
public record PurgaB4LoteResponse(
    int    Candidatos,
    int    Purgados,
    int    RetenidosPorRevisionManual,
    int    YaPurgados,
    int    NoElegibles,
    int    Errores,
    double DuracionMs);
