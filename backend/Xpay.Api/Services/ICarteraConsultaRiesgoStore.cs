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

// M2.3b3 — resultado de un intento de purga de los campos crudos.
public enum ResultadoPurgaIntento
{
    // Se pusieron NULL los 6 crudos y se marcó resultado_purgado_utc.
    Purgado,
    // El intento ya tenía resultado_purgado_utc != NULL — no-op idempotente.
    YaPurgado,
    // El intento no cumple las precondiciones técnicas de purga (no existe,
    // no está FINALIZADO, no vencido respecto a cutoffUtc, o no tiene ningún
    // crudo que purgar).
    NoElegible,
}

// M2.3b3 — INFRAESTRUCTURA DORMIDA. Contrato SEPARADO de
// ICarteraConsultaRiesgoStore: purga (NULL) los 6 campos crudos de MiDecisor
// (con_informacion / score_raw / viabilidad_raw / rating_recaudos_raw /
// monto_sugerido_raw / alertas_count) de un intento y deja una marca de
// auditoría (resultado_purgado_utc).
//
// NO está registrada en DI. NO tiene ningún caller de runtime (scheduler /
// job / endpoint / worker / BackgroundService). NO define período de
// retención. `cutoffUtc` lo provee el llamador.
//
// El gate durable de consumo YA está implementado técnicamente: la purga
// exige `resultado_consumido_utc != NULL` (marca que escribe M2.4a al
// normalizar el resultado a observaciones de la solicitud). Un intento no
// consumido → NoElegible.
//
// NO invocar operacionalmente hasta definir (decisiones externas):
//   - política / duración de retención;
//   - evento a partir del cual empieza a contar el plazo;
//   - invocador autorizado.
public interface ICarteraResultadoRiesgoPurga
{
    // Transacción pequeña bajo AppLock XPAY:CARTERA_RIESGO:{idSolicitud}
    // (owner=Transaction). Re-lee el intento (idSolicitud, numeroIntento)
    // dentro del lock y aplica los guards, en orden: fase == FINALIZADO;
    // resultado_purgado_utc == NULL (si no → YaPurgado);
    // resultado_consumido_utc != NULL (si no → NoElegible — gate de consumo);
    // fecha_fin != NULL y fecha_fin < cutoffUtc; al menos un crudo != NULL.
    // Si todos pasan:
    // NULL de los 6 crudos + set resultado_purgado_utc → Purgado. NO toca
    // resultado_tecnico / es_intento_con_resultado_util / http/content status
    // / fase_intento / fechas originales / numero_intento / idempotency_key /
    // correlation_id, ni ninguna columna de cartera_solicitudes_cupo.
    // Idempotente. Sin retry automático. `cutoffUtc` debe ser UTC.
    Task<ResultadoPurgaIntento> PurgarResultadoIntentoAsync(
        long idSolicitud, int numeroIntento, DateTime cutoffUtc, CancellationToken cancellationToken);
}

// M2.4a — resultado de un intento de CONSUMO durable del resultado MiDecisor.
public enum ResultadoConsumoRiesgo
{
    // Se normalizaron los 6 crudos a observaciones tipadas de la solicitud
    // (con_informacion_observado / score_observado / estado_score /
    // viabilidad_observada / rating_recaudos_observado /
    // monto_sugerido_observado / alertas_count_observado) y se marcó
    // resultado_consumido_utc en el intento, atómicamente.
    Consumido,
    // El intento ya tenía resultado_consumido_utc != NULL — no-op idempotente.
    // La marca durable es autoritativa: NO se re-normaliza ni se "repara".
    YaConsumido,
    // El intento/solicitud no cumple las precondiciones de consumo (no existe,
    // solicitud no EN_EVALUACION, intento no FINALIZADO, intento sin resultado
    // útil, o intento ya purgado).
    NoElegible,
}

// M2.4a — INFRAESTRUCTURA DORMIDA. Contrato SEPARADO de
// ICarteraConsultaRiesgoStore y de ICarteraResultadoRiesgoPurga: convierte de
// forma determinista un intento MiDecisor FINALIZADO con resultado útil en
// observaciones normalizadas y purga-seguras de la solicitud, y marca
// durablemente que ese resultado fue consumido.
//
// NO emite veredicto crediticio: NO toca decision_crediticia / monto_aprobado
// / codigo_motivo_decision / fecha_decision / estado_solicitud /
// id_cupo_ordinario / edad_calculada_al_momento / los snapshots de política, ni
// ninguno de los 6 crudos del intento.
//
// NO está registrada en DI. NO tiene ningún caller de runtime (scheduler /
// job / endpoint / worker / BackgroundService). Se alcanza sólo instanciando
// CarteraConsultaRiesgoStore explícitamente (tests).
public interface ICarteraResultadoRiesgoConsumo
{
    // Transacción pequeña bajo AppLock XPAY:CARTERA_RIESGO:{idSolicitud}
    // (owner=Transaction). Re-lee solicitud + intento (idSolicitud,
    // numeroIntento) dentro del lock y aplica los guards en orden: existencia →
    // resultado_consumido_utc == NULL (si no → YaConsumido) → estado ==
    // EN_EVALUACION → fase == FINALIZADO → es_intento_con_resultado_util == true
    // → resultado_purgado_utc == NULL (todos los NoElegible). Si util == true
    // pero resultado_tecnico no es ACEPTADA/SIN_INFORMACION → invariante
    // (corrupción). Si todos pasan: normaliza (CarteraResultadoRiesgoNormalizer)
    // y persiste el snapshot en cartera_solicitudes_cupo + fecha_actualizacion,
    // y resultado_consumido_utc en el intento, con un único DateTime.UtcNow.
    // Idempotente. Sin retry automático. Sin red.
    Task<ResultadoConsumoRiesgo> ConsumirResultadoRiesgoAsync(
        long idSolicitud, int numeroIntento, CancellationToken cancellationToken = default);
}
