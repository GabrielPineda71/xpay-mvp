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

    public async Task<long> TransferirWalletAsync(TransferenciaWalletRequest request)
    {
        if (request.Valor <= 0)
            throw new InvalidOperationException("El valor de la transferencia debe ser mayor a cero.");
        if (request.IdWalletOrigen == request.IdWalletDestino)
            throw new InvalidOperationException("La wallet origen y destino no pueden ser la misma.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var walletOrigen = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == request.IdWalletOrigen && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet origen no existe o no está activa.");
            var walletDestino = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == request.IdWalletDestino && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet destino no existe o no está activa.");

            if (walletOrigen.TipoWallet != "PERSONA")
                throw new InvalidOperationException("La wallet origen debe ser de tipo PERSONA.");
            if (walletDestino.TipoWallet != "PERSONA")
                throw new InvalidOperationException("La wallet destino debe ser de tipo PERSONA.");

            var saldoOrigen = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == request.IdWalletOrigen)
                ?? throw new InvalidOperationException("La wallet origen no tiene registro de saldo.");
            var saldoDestino = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == request.IdWalletDestino)
                ?? throw new InvalidOperationException("La wallet destino no tiene registro de saldo.");

            if (saldoOrigen.SaldoDisponible < request.Valor)
                throw new InvalidOperationException("Saldo insuficiente en la wallet origen.");

            // Cuenta 210101 = Obligación Wallet Usuarios (PASIVO).
            // La transferencia reasigna la obligación: se debita a origen y se acredita a destino.
            var obligacionOrigen = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == walletOrigen.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210101 para la unidad de negocio origen.");
            var obligacionDestino = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == walletDestino.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210101 para la unidad de negocio destino.");

            var saldoOrigenAntes   = saldoOrigen.SaldoDisponible;
            var saldoOrigenDespues = saldoOrigenAntes - request.Valor;
            var saldoDestinoAntes   = saldoDestino.SaldoDisponible;
            var saldoDestinoDespues = saldoDestinoAntes + request.Valor;
            var now        = DateTime.UtcNow;
            var descripcion = request.Descripcion ?? "Transferencia XPAY a XPAY.";

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio  = walletOrigen.IdUnidadNegocio,
                TipoTransaccion  = "TRANSFERENCIA_WALLET",
                ReferenciaTipo   = "wallets",
                ReferenciaId     = walletOrigen.IdWallet,
                Descripcion      = descripcion,
                ValorTotal       = request.Valor,
                Estado           = "REGISTRADA",
                CreadoPor        = request.CreadoPor,
                FechaTransaccion = now
            };
            _db.LedgerTransacciones.Add(tx);
            await _db.SaveChangesAsync();

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = obligacionOrigen.IdCuenta,
                    Naturaleza          = "D",
                    Valor               = request.Valor,
                    Concepto            = "TRANSFERENCIA_SALIDA",
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = walletOrigen.IdWallet,
                    Descripcion         = $"Débito obligación wallet origen #{walletOrigen.IdWallet}.",
                    FechaMovimiento     = now
                },
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = obligacionDestino.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = request.Valor,
                    Concepto            = "TRANSFERENCIA_ENTRADA",
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = walletDestino.IdWallet,
                    Descripcion         = $"Crédito obligación wallet destino #{walletDestino.IdWallet}.",
                    FechaMovimiento     = now
                }
            );

            _db.WalletMovimientos.AddRange(
                new WalletMovimiento
                {
                    IdWallet            = walletOrigen.IdWallet,
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    TipoMovimiento      = "TRANSFERENCIA_SALIDA",
                    Naturaleza          = "D",
                    Valor               = request.Valor,
                    SaldoAntes          = saldoOrigenAntes,
                    SaldoDespues        = saldoOrigenDespues,
                    Descripcion         = descripcion,
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = walletDestino.IdWallet,
                    Estado              = "APLICADO",
                    CreadoPor           = request.CreadoPor,
                    FechaMovimiento     = now
                },
                new WalletMovimiento
                {
                    IdWallet            = walletDestino.IdWallet,
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    TipoMovimiento      = "TRANSFERENCIA_ENTRADA",
                    Naturaleza          = "C",
                    Valor               = request.Valor,
                    SaldoAntes          = saldoDestinoAntes,
                    SaldoDespues        = saldoDestinoDespues,
                    Descripcion         = descripcion,
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = walletOrigen.IdWallet,
                    Estado              = "APLICADO",
                    CreadoPor           = request.CreadoPor,
                    FechaMovimiento     = now
                }
            );

            saldoOrigen.SaldoDisponible  = saldoOrigenDespues;
            saldoOrigen.FechaActualizacion = now;
            saldoDestino.SaldoDisponible  = saldoDestinoDespues;
            saldoDestino.FechaActualizacion = now;

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario    = request.CreadoPor,
                IdPersona    = walletOrigen.IdPersona,
                Modulo       = "WALLET",
                Accion       = "TRANSFERENCIA",
                Entidad      = "wallets",
                IdEntidad    = walletOrigen.IdWallet.ToString(),
                ValorAnterior = saldoOrigenAntes.ToString("0.00"),
                ValorNuevo   = saldoOrigenDespues.ToString("0.00"),
                Resultado    = "EXITOSO",
                Observacion  = $"Transferencia de {request.Valor:0.00} hacia wallet #{walletDestino.IdWallet}.",
                FechaEvento  = now
            });

            await _db.SaveChangesAsync();

            var totalDebitos = await _db.LedgerMovimientos
                .Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "D")
                .SumAsync(m => m.Valor);
            var totalCreditos = await _db.LedgerMovimientos
                .Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "C")
                .SumAsync(m => m.Valor);
            if (totalDebitos != totalCreditos)
                throw new InvalidOperationException("La transacción ledger de transferencia no está balanceada.");

            await transaction.CommitAsync();
            return tx.IdTransaccionLedger;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
