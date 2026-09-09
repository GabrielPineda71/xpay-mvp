namespace Xpay.Api.Services;

// Resultado de un intento de registrar la aceptación de la autorización de
// consulta en centrales de riesgo.
public enum ResultadoRegistroAutorizacion
{
    // Se insertó una fila de evidencia nueva en cartera_autorizacion_consulta_riesgo.
    Registrada,
    // Ya existía una aceptación para (id_solicitud_origen, version) y es
    // consistente con los valores esperados — no-op idempotente, NO se
    // reescribe ni se duplica.
    YaRegistrada,
    // La solicitud no cumple las precondiciones de captura: no existe, no
    // pertenece al usuario autenticado, no está RECIBIDA, la versión recibida
    // no es la vigente, o la identidad usuario/persona es inconsistente.
    NoElegible,
}

// V1 (ACTA 001 §3 · XPAY-195/196/197) — captura y lectura de la evidencia
// durable de autorización. Superficie INSERT-ONLY: sin update, sin delete.
// NO llama a MiDecisor. NO está registrada en DI (el endpoint la instancia
// con el XpayDbContext scoped ; los tests la instancian explícitamente).
public interface ICarteraAutorizacionConsultaRiesgoStore
{
    // Registra la aceptación del titular para la solicitud `idSolicitud`.
    //
    // Pasos: carga la solicitud → ownership (solicitud.id_usuario == idUsuario)
    // → estado RECIBIDA → resuelve id_persona y verifica
    // usuarios(idUsuario).id_persona == solicitud.id_persona → `versionRecibida`
    // exactamente == V1_Version → toma texto/hash EXCLUSIVAMENTE del recurso
    // backend → fecha UTC del servidor → INSERT.
    //
    // Idempotente: ante violación de UNIQUE(id_solicitud_origen, version) relee
    // la fila y devuelve YaRegistrada SÓLO si es consistente con lo esperado
    // (misma persona/usuario/hash/versión) ; una fila preexistente inconsistente
    // NO se acepta silenciosamente → excepción de invariante (fail-closed).
    Task<ResultadoRegistroAutorizacion> RegistrarAceptacionAsync(
        long idSolicitud, long idUsuario, string versionRecibida,
        string? correlationId, CancellationToken cancellationToken = default);
}
