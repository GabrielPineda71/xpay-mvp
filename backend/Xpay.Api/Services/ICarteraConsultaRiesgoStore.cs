using Xpay.Api.Models;

namespace Xpay.Api.Services;

// M2.3b1 — frontera SQL mínima del orquestador de consulta de riesgo. NO es un
// repositorio genérico: expone exactamente lo que necesita
// CarteraConsultaRiesgoService (pre-flight, TX-A guardada, marca de fase
// ENVIO_INCIERTO, TX-B guardada). Sin IQueryable, sin duplicar
// CarteraOrdinariaService.
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
    // solicitud ya no estaba en RECIBIDA. El estado durable es el guard
    // definitivo a partir de ahí (el lock se libera al Commit).
    Task<bool> IntentarIniciarConsultaAsync(
        long idSolicitud, long idUsuario, DateTime fechaUtc, CancellationToken cancellationToken);

    // M2.3b1 — TX pequeña: marca el intento numero_intento = 1 como
    // ENVIO_INCIERTO ANTES de SendAsync (frontera de no-retry-automático).
    // BeginTransaction → re-lectura tracked de solicitud + intento → guards
    // (solicitud del usuario, estado == CONSULTANDO_RIESGO, intento existe,
    // fase == PRE_CALL, fecha_fin == null, resultado_tecnico == null) →
    // fase = ENVIO_INCIERTO → SaveChanges → Commit. Falla cerrado (throw)
    // si algún guard no se cumple. Sin AppLock (TX-A ya serializó).
    Task MarcarEnvioInciertoAsync(
        long idSolicitud, long idUsuario, DateTime fechaUtc, CancellationToken cancellationToken);

    // M2.3b1 — TX-B ÚNICA guardada: completa el intento numero_intento = 1
    // (resultado_tecnico, http/content status, fecha_fin, es_intento_util,
    // los 6 campos normalizados crudos, fase = FINALIZADO) y transiciona la
    // solicitud a outcome.EstadoSolicitudFinal. Guards con re-lectura tracked
    // dentro de la transacción: solicitud del usuario, estado ==
    // CONSULTANDO_RIESGO, intento existe, fase == ENVIO_INCIERTO,
    // fecha_fin == null, resultado_tecnico == null. Falla cerrado (throw) sin
    // sobrescribir si algún guard no se cumple. NO toca decision_crediticia
    // ni score_observado/monto_sugerido_observado/estado_score/
    // viabilidad_observada/rating_recaudos_observado. Sin AppLock (b1).
    Task FinalizarIntentoAsync(
        long idSolicitud, long idUsuario, ResultadoIntentoDurable outcome, CancellationToken cancellationToken);
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
    DateTime FechaFinUtc,
    // Campos normalizados CRUDOS de MiDecisorResultado — verbatim, sin
    // convertir. Todos NULL cuando no se recibió un MiDecisorResultado
    // (rechazo del proveedor, error de auth/config/protocolo/transporte,
    // o desbordamiento de longitud → ERROR_PROTOCOLO).
    bool?    ConInformacion,
    string?  ScoreRaw,
    string?  ViabilidadRaw,
    string?  RatingRecaudosRaw,
    string?  MontoSugeridoRaw,
    int?     AlertasCount);
