using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-373 FASE 15 — tests offline/puros de WalletReservationCalculator.
// CERO I/O, CERO base de datos.
public class WalletReservationCalculatorTests
{
    // FASE 15 test #7 — reserva: disponible↓ retenido↑, suma preservada.
    [Fact]
    public void TryReserve_SaldoSuficiente_DisminuyeDisponibleYAumentaRetenido()
    {
        var result = WalletReservationCalculator.TryReserve(saldoDisponibleActual: 100_000m, saldoRetenidoActual: 0m, monto: 30_000m);

        Assert.True(result.Success);
        Assert.Equal(70_000m, result.SaldoDisponibleResultante);
        Assert.Equal(30_000m, result.SaldoRetenidoResultante);
        // La suma económica se conserva durante la reserva.
        Assert.Equal(100_000m, result.SaldoDisponibleResultante + result.SaldoRetenidoResultante);
    }

    // FASE 15 test #8 — saldo insuficiente → no reserva.
    [Fact]
    public void TryReserve_SaldoInsuficiente_NoReservaYPreservaValoresOriginales()
    {
        var result = WalletReservationCalculator.TryReserve(saldoDisponibleActual: 10_000m, saldoRetenidoActual: 5_000m, monto: 30_000m);

        Assert.False(result.Success);
        Assert.Equal(10_000m, result.SaldoDisponibleResultante);
        Assert.Equal(5_000m, result.SaldoRetenidoResultante);
        Assert.NotNull(result.MotivoRechazo);
    }

    [Fact]
    public void TryReserve_SaldoExactamenteIgualAlMonto_PermiteLaReserva()
    {
        var result = WalletReservationCalculator.TryReserve(saldoDisponibleActual: 30_000m, saldoRetenidoActual: 0m, monto: 30_000m);

        Assert.True(result.Success);
        Assert.Equal(0m, result.SaldoDisponibleResultante);
        Assert.Equal(30_000m, result.SaldoRetenidoResultante);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void TryReserve_MontoNoPositivo_NoReserva(decimal monto)
    {
        var result = WalletReservationCalculator.TryReserve(saldoDisponibleActual: 100_000m, saldoRetenidoActual: 0m, monto: monto);

        Assert.False(result.Success);
    }

    // FASE 15 test #9 (parte aritmética) — SETTLED: retenido↓, disponible
    // NUNCA vuelve a tocarse aquí (ya bajó en la reserva).
    [Fact]
    public void Settle_DisminuyeSoloElRetenido()
    {
        var result = WalletReservationCalculator.Settle(saldoRetenidoActual: 30_000m, monto: 30_000m);

        Assert.Equal(0m, result.SaldoRetenidoResultante);
    }

    // FASE 15 test #11 (parte aritmética) — REJECTED: retenido↓,
    // disponible↑, monto devuelto íntegro.
    [Fact]
    public void Release_DevuelveElMontoAlDisponibleYReduceElRetenido()
    {
        var result = WalletReservationCalculator.Release(saldoDisponibleActual: 70_000m, saldoRetenidoActual: 30_000m, monto: 30_000m);

        Assert.Equal(100_000m, result.SaldoDisponibleResultante);
        Assert.Equal(0m, result.SaldoRetenidoResultante);
    }

    [Fact]
    public void Release_PreservaLaSumaEconomica()
    {
        var antes = 70_000m + 30_000m;
        var result = WalletReservationCalculator.Release(saldoDisponibleActual: 70_000m, saldoRetenidoActual: 30_000m, monto: 30_000m);

        Assert.Equal(antes, result.SaldoDisponibleResultante + result.SaldoRetenidoResultante);
    }
}
