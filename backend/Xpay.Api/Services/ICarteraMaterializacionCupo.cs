namespace Xpay.Api.Services;

// M2.4c / TX2 — resultado de un intento de MATERIALIZACIÓN durable del cupo de
// Cartera Ordinaria para una solicitud YA APROBADA.
public enum ResultadoMaterializacionCupo
{
    // Se creó o se actualizó el cupo ordinario del usuario, se enlazó la
    // solicitud (id_cupo_ordinario + fecha_materializacion_cupo) y la solicitud
    // pasó de APROBADA_PENDIENTE_CUPO a APROBADA, atómicamente.
    Materializado,

    // La solicitud ya tenía id_cupo_ordinario + fecha_materializacion_cupo
    // consistentes — no-op idempotente. La marca durable es autoritativa: NO se
    // re-materializa ni se "repara".
    YaMaterializado,

    // La solicitud/cupo no cumple las precondiciones de materialización: la
    // solicitud no está en APROBADA_PENDIENTE_CUPO, o (en creación) el usuario
    // no tiene una wallet PERSONA ACTIVA.
    NoElegible,

    // El usuario YA tiene un cupo ordinario pero está SUSPENDIDO o CANCELADO.
    // TX2 NO lo reactiva automáticamente: la solicitud permanece
    // APROBADA_PENDIENTE_CUPO para resolución manual.
    CupoNoActivo,
}

// M2.4c / TX2 — INFRAESTRUCTURA DORMIDA. Contrato SEPARADO: dada una solicitud
// de Cartera Ordinaria YA APROBADA (estado APROBADA_PENDIENTE_CUPO,
// decision_crediticia = APROBADA, monto_aprobado válido), materializa de forma
// determinista su cupo ordinario aplicando la política de producto autorizada:
//
//   - un solo cupo ordinario por usuario (respaldado por UNIQUE(id_usuario));
//   - sin cupo → crear con cupo_aprobado = monto_aprobado;
//   - con cupo ACTIVO → reutilizar el mismo id_cupo;
//     cupo_aprobado = MAX(cupo_aprobado_actual, monto_aprobado) (nunca reduce);
//     cupo_usado NUNCA se toca;
//   - con cupo SUSPENDIDO/CANCELADO → CupoNoActivo (no reactiva).
//
// Enlaza la solicitud (id_cupo_ordinario + fecha_materializacion_cupo) y la
// transiciona APROBADA_PENDIENTE_CUPO → APROBADA. Todo-o-nada bajo AppLock.
//
// NO emite veredicto crediticio: NO calcula ni escribe monto_aprobado /
// decision_crediticia / fecha_decision / codigo_motivo_decision / observados de
// riesgo / edad. NO realiza utilización, desembolso ni movimiento financiero
// (0 escrituras a ledger_* / wallet_movimientos / cartera_utilizaciones).
// APLICA EXCLUSIVAMENTE a Cartera Ordinaria: 0 lecturas / 0 escrituras a
// libranza_*.
//
// NO está registrada en DI. NO tiene ningún caller de runtime (scheduler /
// job / endpoint / worker / BackgroundService). Se alcanza sólo instanciando
// CarteraMaterializacionCupoStore explícitamente (tests).
public interface ICarteraMaterializacionCupo
{
    // Transacción pequeña bajo AppLock XPAY:CARTERA_CUPO:{idUsuario}
    // (owner=Transaction). Primera lectura mínima sólo para derivar idUsuario;
    // tras el lock, re-lectura AUTORITATIVA de la solicitud. Guards en orden:
    // existencia → marca durable (id_cupo_ordinario / fecha_materializacion_cupo,
    // fail-closed sin auto-repair) → estado == APROBADA_PENDIENTE_CUPO →
    // coherencia decision_crediticia/monto_aprobado (corrupción → invariante) →
    // cupo existente (SUSPENDIDO/CANCELADO → CupoNoActivo) → wallet PERSONA
    // ACTIVA (sólo creación; falta → NoElegible). Idempotente. Sin retry
    // automático. Sin red.
    Task<ResultadoMaterializacionCupo> MaterializarCupoAsync(
        long idSolicitud,
        CancellationToken cancellationToken = default);
}

// M2.4c / TX2 — corrupción durable detectada al materializar: un estado que la
// clasificación del (futuro) motor de decisión no puede haber producido para
// una solicitud realmente aprobada — decision_crediticia != APROBADA junto con
// estado APROBADA_PENDIENTE_CUPO; monto_aprobado NULL o <= 0 en una solicitud
// aprobada; marca de materialización parcial o inconsistente
// (id_cupo_ordinario sin fecha, fecha sin id, o id que apunta a un cupo
// inexistente o de otro usuario). Fail-closed: se lanza, NO se persiste, sin
// retry.
public sealed class CarteraMaterializacionInvarianteException : Exception
{
    public CarteraMaterializacionInvarianteException(string message)
        : base(message)
    {
    }
}
