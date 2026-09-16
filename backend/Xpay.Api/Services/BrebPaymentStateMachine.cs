namespace Xpay.Api.Services;

// XPAY-373 FASE 1/9/11 — clasificación de estados Payment Bre-B y decisión
// de transición del retiro local. Función PURA — no hace I/O, no toca la
// base de datos, no llama a Passport. Diseñada para ser la ÚNICA función de
// decisión financiera reutilizada tanto por polling (GET /v1/payments/
// {payment_id}, XPAY-373 FASE 10) como por un futuro webhook (XPAY-373
// FASE 11 — "ApplyPassportPaymentStatus... no duplicar lógica financiera").
//
// CLASIFICACIÓN — reconfirmada en XPAY-373 vía consulta de solo lectura a
// docs.passportfintech.com (nunca inferida ni inventada):
//   - "PENDING"    → TRANSITORIO. Confirmado textualmente en
//                    /EN/initiate-a-payment: "The payment status will
//                    first be PENDING."
//   - "PROCESSING" → TRANSITORIO. Confirmado textualmente en /EN/webhooks:
//                    "not the final state of the payment" — advierte
//                    esperar SETTLED o REJECTED.
//   - "SETTLED"    → FINAL_EXITOSO. Confirmado en /EN/initiate-a-payment
//                    ("Once the payment is processed, it will change to
//                    SETTLED") y en /EN/webhooks (payment.outbound.settled
//                    = "marks the successful transfer of funds... funds
//                    are now available").
//   - "REJECTED"   → FINAL_FALLIDO. Confirmado EXPLÍCITAMENTE en
//                    /EN/webhooks (payment.outbound.rejected): "this
//                    status is final" + "the movement of funds will not
//                    take place".
//   - cualquier otro string (incluido null/vacío) → DESCONOCIDO, tratado
//     EXACTAMENTE igual que TRANSITORIO (nunca mueve dinero) — un status
//     no documentado (o uno nuevo que Passport agregue en el futuro) jamás
//     debe interpretarse por default como éxito ni como fallo.
//
// NOTA DE DISCREPANCIA DOCUMENTAL (no oculta): el ejemplo JSON de la propia
// página /EN/initiate-a-payment muestra `"status": "PROCESSING"` en la
// respuesta de creación, pese a que el texto de esa misma página dice que
// el estado "first" es PENDING. XPAY-373 NO resuelve esta discrepancia por
// inferencia — es irrelevante para la seguridad financiera del código,
// porque AMBOS valores se clasifican como TRANSITORIO: cualquiera que sea
// el status inicial real, el código nunca mueve dinero hasta ver SETTLED o
// REJECTED explícitamente.
public static class BrebPaymentStateMachine
{
    public enum PaymentStatusClass
    {
        Transitorio,
        FinalExitoso,
        FinalFallido,
        Desconocido,
    }

    public const string StatusPending    = "PENDING";
    public const string StatusProcessing = "PROCESSING";
    public const string StatusSettled    = "SETTLED";
    public const string StatusRejected   = "REJECTED";

    public static PaymentStatusClass Classify(string? status) => status switch
    {
        StatusSettled  => PaymentStatusClass.FinalExitoso,
        StatusRejected => PaymentStatusClass.FinalFallido,
        StatusPending or StatusProcessing => PaymentStatusClass.Transitorio,
        _ => PaymentStatusClass.Desconocido,
    };

    // Estados LOCALES de PassportBrebRetiro reutilizados de la migración
    // 010 (CHECK constraint ya los permitía desde Fase 64) — XPAY-373 no
    // agrega ningún estado nuevo.
    public const string RetiroEstadoLiquidado  = "LIQUIDADO";
    public const string RetiroEstadoRechazado  = "RECHAZADO";

    public enum RetiroTransitionAction
    {
        // Idempotencia (FASE 7/8, tests 10/12): el retiro YA está en un
        // estado local terminal — cualquier notificación adicional
        // (polling repetido, webhook duplicado) no debe volver a mover
        // dinero.
        NoOpYaFinalizado,
        // FASE 9: mantener el monto en SaldoRetenido, sin liberar ni
        // debitar — cubre TRANSITORIO y DESCONOCIDO por igual.
        MantenerRetenido,
        // FASE 7: SETTLED confirmado — débito definitivo (SaldoRetenido
        // baja, SaldoDisponible NO vuelve a tocarse).
        FinalizarLiquidado,
        // FASE 8: REJECTED confirmado — liberar reserva íntegra.
        LiberarRechazado,
    }

    public static RetiroTransitionAction Decide(string retiroEstadoActual, PaymentStatusClass incoming)
    {
        ArgumentNullException.ThrowIfNull(retiroEstadoActual);

        if (retiroEstadoActual == RetiroEstadoLiquidado || retiroEstadoActual == RetiroEstadoRechazado)
            return RetiroTransitionAction.NoOpYaFinalizado;

        return incoming switch
        {
            PaymentStatusClass.FinalExitoso => RetiroTransitionAction.FinalizarLiquidado,
            PaymentStatusClass.FinalFallido => RetiroTransitionAction.LiberarRechazado,
            _ => RetiroTransitionAction.MantenerRetenido,
        };
    }
}
