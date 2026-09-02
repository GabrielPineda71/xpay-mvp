using Xpay.Api.Models;

namespace Xpay.Api.Services;

// M2.3a — frontera SQL mínima del orquestador de consulta de riesgo. NO es un
// repositorio genérico: expone exactamente las tres operaciones que necesita
// CarteraConsultaRiesgoService (pre-flight, TX-A guardada, TX-B). Sin
// IQueryable, sin duplicar CarteraOrdinariaService.
public interface ICarteraConsultaRiesgoStore
{
    // Pre-flight (SIN transacción). Devuelve null si la solicitud no existe o
    // no pertenece a idUsuario (no se distingue, para no revelar existencia).
    Task<ConsultaRiesgoContexto?> CargarContextoAsync(
        long idSolicitud, long idUsuario, CancellationToken cancellationToken);

    // TX-A: BeginTransaction → sp_getapplock (owner=Transaction) por
    // idSolicitud → re-lectura dentro del lock → si estado == RECIBIDA:
    // set CONSULTANDO_RIESGO + fecha_actualizacion → SaveChanges → Commit.
    // Devuelve true SÓLO si esta ejecución ganó la transición; false si la
    // solicitud ya no estaba en RECIBIDA (otra ejecución ganó, o estado
    // inesperado). El lock se libera al Commit — el estado durable es el
    // guard definitivo a partir de ahí.
    Task<bool> IntentarIniciarConsultaAsync(
        long idSolicitud, long idUsuario, DateTime fechaUtc, CancellationToken cancellationToken);

    // TX-B: completa el intento numero_intento = 1 (resultado_tecnico,
    // http_status_observado, content_status_observado, fecha_fin,
    // es_intento_con_resultado_util) y transiciona la solicitud a
    // outcome.EstadoSolicitudFinal. NO toca decision_crediticia ni columnas
    // de score/monto.
    Task CompletarIntentoAsync(
        long idSolicitud, ResultadoIntentoDurable outcome, CancellationToken cancellationToken);
}

public sealed record ConsultaRiesgoContexto(
    long IdSolicitud,
    long IdUsuario,
    long IdPersona,
    string EstadoSolicitud,
    Persona? Persona);

public sealed record ResultadoIntentoDurable(
    string   EstadoSolicitudFinal,
    string   ResultadoTecnico,
    int?     HttpStatusObservado,
    string?  ContentStatusObservado,
    bool     EsResultadoUtil,
    DateTime FechaFinUtc);
