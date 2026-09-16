namespace Xpay.Api.Services;

// XPAY-373 FASE 4 — aritmética PURA de reserva/liberación/liquidación de
// wallet_saldos.SaldoRetenido (primer uso REAL de esa columna — hasta
// XPAY-372 existía en el esquema pero ningún código la usaba). Función
// pura — no hace I/O, no toca la base de datos, no aplica ningún lock.
//
// LA SEGURIDAD DE CONCURRENCIA (dos retiros simultáneos que juntos exceden
// el saldo) NO vive aquí — vive en el llamador (BrebPaymentService), que
// debe leer la fila `wallet_saldos` con `WITH (UPDLOCK, ROWLOCK)` ANTES de
// invocar TryReserve, exactamente el mismo patrón ya maduro y probado en
// WalletOperacionService.TransferirWalletAsync/RecargarWalletManualAsync.
// Esta clase sólo garantiza que, DADO un snapshot ya bloqueado, la
// aritmética es correcta y preserva
// SaldoDisponible + SaldoRetenido en cada operación.
public static class WalletReservationCalculator
{
    public readonly record struct ReservationResult(
        bool Success, decimal SaldoDisponibleResultante, decimal SaldoRetenidoResultante, string? MotivoRechazo);

    public static ReservationResult TryReserve(decimal saldoDisponibleActual, decimal saldoRetenidoActual, decimal monto)
    {
        if (monto <= 0)
            return new ReservationResult(
                false, saldoDisponibleActual, saldoRetenidoActual, "El monto debe ser mayor a cero.");

        if (saldoDisponibleActual < monto)
            return new ReservationResult(
                false, saldoDisponibleActual, saldoRetenidoActual,
                $"Saldo insuficiente. Disponible: {saldoDisponibleActual:0.00}, solicitado: {monto:0.00}.");

        return new ReservationResult(true, saldoDisponibleActual - monto, saldoRetenidoActual + monto, null);
    }

    // FASE 7/8 — al liquidar (SETTLED), SaldoDisponible NO vuelve a
    // tocarse: ya se redujo en TryReserve. Sólo SaldoRetenido baja.
    public readonly record struct SettleResult(decimal SaldoRetenidoResultante);

    public static SettleResult Settle(decimal saldoRetenidoActual, decimal monto) =>
        new(saldoRetenidoActual - monto);

    // FASE 8/5 — al liberar (REJECTED, o fallo local antes de enviar a
    // Passport), el monto vuelve íntegro a SaldoDisponible.
    public readonly record struct ReleaseResult(decimal SaldoDisponibleResultante, decimal SaldoRetenidoResultante);

    public static ReleaseResult Release(decimal saldoDisponibleActual, decimal saldoRetenidoActual, decimal monto) =>
        new(saldoDisponibleActual + monto, saldoRetenidoActual - monto);
}
