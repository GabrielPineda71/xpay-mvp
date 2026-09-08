namespace Xpay.Api.Services;

// M2.4b — resultado de un intento de APLICAR la decisión crediticia a una
// solicitud de cupo.
public enum ResultadoAplicacionDecision
{
    // Se evaluó el snapshot durable, se persistió el veredicto en
    // cartera_solicitudes_cupo (decision_crediticia / estado_solicitud /
    // monto_aprobado / codigo_motivo_decision / fecha_decision /
    // senal_posible_suplantacion) + las 0/N filas ordenadas de
    // cartera_solicitud_cupo_motivos_decision, atómicamente.
    Aplicada,
    // La solicitud ya tenía una decisión durable final (fecha_decision != NULL
    // y decision_crediticia != PENDIENTE, coherentes) — no-op idempotente. NO
    // se re-evalúa, NO se reescribe, NO se borran/reinsertan motivos.
    YaDecidido,
    // La solicitud no cumple las precondiciones (no existe, no está
    // EN_EVALUACION, o su resultado de riesgo aún no fue consumido durablemente
    // por M2.4a).
    NoElegible,
}

// M2.4b — INFRAESTRUCTURA DORMIDA. Contrato SEPARADO: dado el snapshot durable
// M2.4a de una solicitud EN_EVALUACION cuyo resultado de riesgo ya fue
// consumido, ejecuta el motor de decisión puro (CarteraDecisionEngine) y
// persiste el veredicto + los motivos ordenados, todo-o-nada bajo AppLock.
//
// NO llama a MiDecisor. NO está registrada en DI. NO tiene ningún caller de
// runtime (scheduler / job / endpoint / worker / BackgroundService). Se alcanza
// sólo instanciando CarteraDecisionCrediticiaStore explícitamente (tests).
//
// NO materializa cupo (eso es M2.4c, TX2, otra sub-fase). Para APROBADA deja la
// solicitud en APROBADA_PENDIENTE_CUPO ; M2.4c la tomará después.
public interface ICarteraDecisionCrediticia
{
    // Transacción pequeña bajo AppLock XPAY:CARTERA_RIESGO:{idSolicitud}
    // (owner=Transaction). Re-lee la solicitud (tracked) dentro del lock y aplica
    // los guards en orden: existencia → decisión durable (fecha_decision /
    // decision_crediticia coherentes → YaDecidido ; incoherentes → invariante) →
    // estado == EN_EVALUACION → intento numero_intento=1 con resultado_consumido_utc
    // != NULL. Si todos pasan: evalúa y persiste con un único DateTime.UtcNow
    // (fuera del motor puro). Idempotente. Sin retry automático. Sin red.
    Task<ResultadoAplicacionDecision> AplicarDecisionAsync(
        long idSolicitud, CancellationToken cancellationToken = default);
}
