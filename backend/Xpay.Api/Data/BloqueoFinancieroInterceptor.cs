using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xpay.Api.Common;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Data;

// Barrera de datos del bloqueo financiero: con el interruptor cerrado
// (ControlOperacionesFinancieras.EstaHabilitado() == false) ningún
// SaveChanges puede registrar dinero ni obligaciones, sea cual sea el
// endpoint, proceso automático o webhook que lo intente. Se lanza antes de
// enviar nada a la base; la transacción del llamador se revierte entera.
//
// Escrituras financieras detectadas:
//   · cualquier alta, cambio o borrado de ledger_transacciones,
//     ledger_movimientos o wallet_movimientos;
//   · un cambio real en cualquier saldo de wallet_saldos, o un alta con saldo
//     distinto de cero (el alta con saldo 0 de una wallet nueva SÍ se permite);
//   · un cupo de cartera nuevo o un cambio de su cupo aprobado/usado/estado, y
//     una utilización de cartera nueva.
//
// Solo cubre lo que pasa por el change tracker: la aplicación no escribe en
// esas tablas con SQL directo (lo vigila BloqueoFinancieroArquitecturaTests).
public sealed class BloqueoFinancieroInterceptor(ControlOperacionesFinancieras control) : SaveChangesInterceptor
{
    private static readonly string[] ColumnasSaldo =
    [
        nameof(WalletSaldo.SaldoDisponible), nameof(WalletSaldo.SaldoRetenido),
        nameof(WalletSaldo.SaldoTransito), nameof(WalletSaldo.SaldoContingencia),
    ];

    private static readonly string[] ColumnasCupo =
    [
        nameof(CarteraCupoOrdinario.CupoAprobado), nameof(CarteraCupoOrdinario.CupoUsado),
        nameof(CarteraCupoOrdinario.Estado),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Verificar(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Verificar(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Verificar(DbContext? context)
    {
        if (context is null || control.EstaHabilitado()) return;
        if (HayEscrituraFinanciera(context.ChangeTracker))
            control.ExigirInterruptor(); // lanza OperacionFinancieraBloqueadaException (Pausa)
    }

    internal static bool HayEscrituraFinanciera(ChangeTracker tracker)
    {
        foreach (var e in tracker.Entries())
        {
            if (e.State is EntityState.Unchanged or EntityState.Detached) continue;

            switch (e.Entity)
            {
                case LedgerTransaccion or LedgerMovimiento or WalletMovimiento:
                    return true;
                case WalletSaldo when e.State == EntityState.Added:
                    if (ColumnasSaldo.Any(c => (decimal)e.Property(c).CurrentValue! != 0m)) return true;
                    break;
                case WalletSaldo:
                    if (e.State == EntityState.Deleted || ColumnasSaldo.Any(c => Cambio(e, c))) return true;
                    break;
                case CarteraCupoOrdinario:
                    if (e.State != EntityState.Modified || ColumnasCupo.Any(c => Cambio(e, c))) return true;
                    break;
                case CarteraUtilizacion:
                    return true;
            }
        }
        return false;
    }

    private static bool Cambio(EntityEntry e, string propiedad)
    {
        var p = e.Property(propiedad);
        return p.IsModified && !Equals(p.OriginalValue, p.CurrentValue);
    }
}
