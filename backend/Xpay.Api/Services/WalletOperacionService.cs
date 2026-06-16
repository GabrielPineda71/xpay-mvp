using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class WalletOperacionService
{
    private readonly XpayDbContext _db;
    public WalletOperacionService(XpayDbContext db) => _db = db;

    public async Task<long> RecargarWalletManualAsync(long idWallet, RecargaWalletRequest request)
    {
        if (request.Valor <= 0) throw new InvalidOperationException("El valor de la recarga debe ser mayor a cero.");
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == idWallet && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet no existe o no está activa.");
            if (wallet.TipoWallet != "PERSONA") throw new InvalidOperationException("La recarga manual inicial solo está permitida para wallets de persona.");
            var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == idWallet)
                ?? throw new InvalidOperationException("La wallet no tiene registro de saldo.");

            var banco = await _db.LedgerCuentas.FirstOrDefaultAsync(c => c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "110202" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger Banco Liquidez Usuarios.");
            var obligacion = await _db.LedgerCuentas.FirstOrDefaultAsync(c => c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger Obligación Wallet Usuarios.");

            var saldoAntes = saldo.SaldoDisponible;
            var saldoDespues = saldoAntes + request.Valor;
            var now = DateTime.UtcNow;

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio = wallet.IdUnidadNegocio,
                TipoTransaccion = "RECARGA_WALLET",
                ReferenciaTipo = "wallets",
                ReferenciaId = wallet.IdWallet,
                Descripcion = request.Observacion ?? "Recarga manual de wallet.",
                ValorTotal = request.Valor,
                Estado = "REGISTRADA",
                CreadoPor = request.CreadoPor,
                FechaTransaccion = now
            };
            _db.LedgerTransacciones.Add(tx);
            await _db.SaveChangesAsync();

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento { IdTransaccionLedger = tx.IdTransaccionLedger, IdCuenta = banco.IdCuenta, Naturaleza = "D", Valor = request.Valor, Concepto = "RECARGA_WALLET", ReferenciaTipo = "wallets", ReferenciaId = wallet.IdWallet, Descripcion = "Entrada de dinero al fondo de liquidez de usuarios.", FechaMovimiento = now },
                new LedgerMovimiento { IdTransaccionLedger = tx.IdTransaccionLedger, IdCuenta = obligacion.IdCuenta, Naturaleza = "C", Valor = request.Valor, Concepto = "RECARGA_WALLET", ReferenciaTipo = "wallets", ReferenciaId = wallet.IdWallet, Descripcion = "Aumento de obligación wallet usuarios.", FechaMovimiento = now }
            );

            var wm = new WalletMovimiento
            {
                IdWallet = wallet.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                TipoMovimiento = "RECARGA",
                Naturaleza = "C",
                Valor = request.Valor,
                SaldoAntes = saldoAntes,
                SaldoDespues = saldoDespues,
                Descripcion = request.Observacion ?? "Recarga manual de wallet.",
                ReferenciaTipo = "ledger_transacciones",
                ReferenciaId = tx.IdTransaccionLedger,
                Estado = "APLICADO",
                CreadoPor = request.CreadoPor,
                FechaMovimiento = now
            };
            _db.WalletMovimientos.Add(wm);

            saldo.SaldoDisponible = saldoDespues;
            saldo.FechaActualizacion = now;

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario = request.CreadoPor,
                IdPersona = wallet.IdPersona,
                Modulo = "WALLET",
                Accion = "RECARGA_MANUAL",
                Entidad = "wallets",
                IdEntidad = wallet.IdWallet.ToString(),
                ValorAnterior = saldoAntes.ToString("0.00"),
                ValorNuevo = saldoDespues.ToString("0.00"),
                Resultado = "EXITOSO",
                Observacion = $"Recarga manual por valor {request.Valor:0.00}. Referencia: {request.ReferenciaExterna}",
                FechaEvento = now
            });

            await _db.SaveChangesAsync();
            var totalDebitos = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "D").SumAsync(m => m.Valor);
            var totalCreditos = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "C").SumAsync(m => m.Valor);
            if (totalDebitos != totalCreditos) throw new InvalidOperationException("La transacción ledger no está balanceada.");

            await transaction.CommitAsync();
            return wm.IdMovimientoWallet;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
