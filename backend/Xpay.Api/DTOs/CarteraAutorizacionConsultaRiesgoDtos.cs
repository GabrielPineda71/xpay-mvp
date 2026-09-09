namespace Xpay.Api.DTOs;

// ── Cartera Ordinaria — Autorización de consulta en centrales de riesgo (V1) ──
// ACTA 001 §3 · XPAY-195/196/197.

// Respuesta de GET .../autorizacion-consulta-riesgo/texto-vigente.
// El texto y el hash provienen EXCLUSIVAMENTE del recurso backend
// (CarteraAutorizacionConsultaRiesgoTextos). El frontend nunca lo hardcodea.
public record AutorizacionConsultaRiesgoTextoResponse(
    string Version,
    string Texto,
    string Hash);

// Body de POST .../solicitudes/{idSolicitud}/autorizar-consulta-riesgo.
// SÓLO contiene la versión que el cliente mostró. idUsuario / idPersona se
// derivan del contexto autenticado, NUNCA del body. El servidor revalida que
// Version sea la vigente y snapshotea el texto desde el recurso.
public record RegistrarAutorizacionConsultaRiesgoRequest(
    string Version);

// Respuesta de POST — resultado de la captura.
public record RegistrarAutorizacionConsultaRiesgoResponse(
    long   IdSolicitud,
    string Version,
    string Resultado); // "registrada" | "ya_registrada"
