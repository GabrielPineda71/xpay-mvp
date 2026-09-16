using Xpay.Api.Services;
using Xunit;
using static Xpay.Api.Services.BrebPaymentStateMachine;

namespace Xpay.Api.Tests.Services;

// XPAY-373 FASE 15 — tests offline/puros de BrebPaymentStateMachine. CERO
// I/O, CERO base de datos, CERO red.
public class BrebPaymentStateMachineTests
{
    // ── Classify — reconfirmado documentalmente en XPAY-373 FASE 1 ───────

    [Fact]
    public void Classify_Settled_EsFinalExitoso() =>
        Assert.Equal(PaymentStatusClass.FinalExitoso, Classify(StatusSettled));

    [Fact]
    public void Classify_Rejected_EsFinalFallido() =>
        Assert.Equal(PaymentStatusClass.FinalFallido, Classify(StatusRejected));

    [Fact]
    public void Classify_Pending_EsTransitorio() =>
        Assert.Equal(PaymentStatusClass.Transitorio, Classify(StatusPending));

    [Fact]
    public void Classify_Processing_EsTransitorio() =>
        Assert.Equal(PaymentStatusClass.Transitorio, Classify(StatusProcessing));

    // FASE 15 test #6 — status desconocido → tratado igual que transitorio
    // (nunca mueve dinero).
    [Theory]
    [InlineData("ACCEPTED")]
    [InlineData("CANCELLED")]
    [InlineData("algo-no-documentado")]
    [InlineData(null)]
    [InlineData("")]
    public void Classify_StatusNoDocumentado_EsDesconocido(string? status) =>
        Assert.Equal(PaymentStatusClass.Desconocido, Classify(status));

    // ── Decide — idempotencia + nunca mover dinero ante duda ─────────────

    [Fact]
    public void Decide_Desconocido_MantieneRetenido() =>
        Assert.Equal(RetiroTransitionAction.MantenerRetenido,
            Decide("ENVIADO_PASSPORT", PaymentStatusClass.Desconocido));

    // FASE 15 test #13/#14 — PENDING/PROCESSING → retenido permanece.
    [Fact]
    public void Decide_Transitorio_MantieneRetenido() =>
        Assert.Equal(RetiroTransitionAction.MantenerRetenido,
            Decide("ENVIADO_PASSPORT", PaymentStatusClass.Transitorio));

    // FASE 15 test #9 — SETTLED confirmado → finaliza liquidado.
    [Fact]
    public void Decide_FinalExitoso_DesdeEnviadoPassport_Liquida() =>
        Assert.Equal(RetiroTransitionAction.FinalizarLiquidado,
            Decide("ENVIADO_PASSPORT", PaymentStatusClass.FinalExitoso));

    // FASE 15 test #10 — SETTLED duplicado (retiro ya LIQUIDADO) → no-op,
    // nunca un segundo débito.
    [Fact]
    public void Decide_FinalExitoso_RetiroYaLiquidado_EsNoOp() =>
        Assert.Equal(RetiroTransitionAction.NoOpYaFinalizado,
            Decide(RetiroEstadoLiquidado, PaymentStatusClass.FinalExitoso));

    // FASE 15 test #11 — REJECTED confirmado → libera reserva.
    [Fact]
    public void Decide_FinalFallido_DesdeEnviadoPassport_Libera() =>
        Assert.Equal(RetiroTransitionAction.LiberarRechazado,
            Decide("ENVIADO_PASSPORT", PaymentStatusClass.FinalFallido));

    // FASE 15 test #12 — REJECTED duplicado (retiro ya RECHAZADO) → no-op,
    // nunca una segunda devolución.
    [Fact]
    public void Decide_FinalFallido_RetiroYaRechazado_EsNoOp() =>
        Assert.Equal(RetiroTransitionAction.NoOpYaFinalizado,
            Decide(RetiroEstadoRechazado, PaymentStatusClass.FinalFallido));

    // Idempotencia cruzada: un retiro ya LIQUIDADO que reciba (por error o
    // reintento tardío) un REJECTED nunca debe revertirse — sigue siendo
    // NoOp, nunca LiberarRechazado.
    [Fact]
    public void Decide_FinalFallido_RetiroYaLiquidado_EsNoOp_NuncaRevierte() =>
        Assert.Equal(RetiroTransitionAction.NoOpYaFinalizado,
            Decide(RetiroEstadoLiquidado, PaymentStatusClass.FinalFallido));

    [Fact]
    public void Decide_FinalExitoso_RetiroYaRechazado_EsNoOp_NuncaRevierte() =>
        Assert.Equal(RetiroTransitionAction.NoOpYaFinalizado,
            Decide(RetiroEstadoRechazado, PaymentStatusClass.FinalExitoso));

    [Fact]
    public void Decide_DesdePendienteEnvioPassport_TransitorioMantieneRetenido() =>
        Assert.Equal(RetiroTransitionAction.MantenerRetenido,
            Decide("PENDIENTE_ENVIO_PASSPORT", PaymentStatusClass.Transitorio));
}
