using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class ReportesService
{
    private readonly XpayDbContext _db;
    public ReportesService(XpayDbContext db) => _db = db;

    public async Task<object> GetEstadoCuentaWalletAsync(long idWallet)
    {
        if (idWallet <= 0)
            throw new InvalidOperationException("El identificador de wallet debe ser mayor a cero.");

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == idWallet)
            ?? throw new InvalidOperationException($"No existe la wallet con id {idWallet}.");

        var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == idWallet);

        var movimientos = await _db.WalletMovimientos
            .Where(m => m.IdWallet == idWallet)
            .OrderByDescending(m => m.FechaMovimiento)
            .Select(m => new
            {
                idMovimiento    = m.IdMovimientoWallet,
                fecha           = m.FechaMovimiento,
                tipoMovimiento  = m.TipoMovimiento,
                naturaleza      = m.Naturaleza,
                valor           = m.Valor,
                saldoAntes      = m.SaldoAntes,
                saldoDespues    = m.SaldoDespues,
                descripcion     = m.Descripcion,
                referenciaTipo  = m.ReferenciaTipo,
                referenciaId    = m.ReferenciaId
            })
            .ToListAsync();

        return new
        {
            idWallet        = wallet.IdWallet,
            tipoWallet      = wallet.TipoWallet,
            nombreWallet    = wallet.NombreWallet,
            estado          = wallet.Estado,
            saldoDisponible = saldo?.SaldoDisponible ?? 0m,
            movimientos
        };
    }

    public async Task<object> GetResumenComercioAsync(long idComercio)
    {
        if (idComercio <= 0)
            throw new InvalidOperationException("El identificador del comercio debe ser mayor a cero.");

        var comercio = await _db.Comercios.FirstOrDefaultAsync(c => c.IdComercio == idComercio)
            ?? throw new InvalidOperationException($"No existe el comercio con id {idComercio}.");

        decimal saldoDisponible = 0m;
        long idWalletComercio = 0;

        if (comercio.IdWalletComercio.HasValue)
        {
            idWalletComercio = comercio.IdWalletComercio.Value;
            var saldoWallet  = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == idWalletComercio);
            saldoDisponible  = saldoWallet?.SaldoDisponible ?? 0m;
        }

        var ventasGrupos = await _db.VentasQr
            .Where(v => v.IdComercio == idComercio)
            .GroupBy(v => v.Estado)
            .Select(g => new { Estado = g.Key, Count = g.Count(), Valor = g.Sum(v => v.ValorBruto) })
            .ToListAsync();

        int vqTotal        = ventasGrupos.Sum(g => g.Count);
        int vqContingencia = ventasGrupos.Where(g => g.Estado == "CONTINGENCIA").Sum(g => g.Count);
        int vqLiquidadas   = ventasGrupos.Where(g => g.Estado == "LIQUIDADA").Sum(g => g.Count);
        decimal vqValor    = ventasGrupos.Sum(g => g.Valor);

        var liquidaciones = await _db.LiquidacionesComercios
            .Where(l => l.IdComercio == idComercio)
            .GroupBy(l => l.IdComercio)
            .Select(g => new { Count = g.Count(), Valor = g.Sum(l => l.ValorNeto) })
            .FirstOrDefaultAsync();

        var retirosGrupos = await _db.RetirosComercio
            .Where(r => r.IdComercio == idComercio)
            .GroupBy(r => r.Estado)
            .Select(g => new { Estado = g.Key, Count = g.Count(), Valor = g.Sum(r => r.Valor) })
            .ToListAsync();

        int retTotal      = retirosGrupos.Sum(g => g.Count);
        int retPendientes = retirosGrupos.Where(g => g.Estado == "PENDIENTE").Sum(g => g.Count);
        int retPagados    = retirosGrupos.Where(g => g.Estado == "PAGADO").Sum(g => g.Count);
        int retRechazados = retirosGrupos.Where(g => g.Estado == "RECHAZADO").Sum(g => g.Count);
        decimal valPend   = retirosGrupos.Where(g => g.Estado == "PENDIENTE").Sum(g => g.Valor);
        decimal valPagado = retirosGrupos.Where(g => g.Estado == "PAGADO").Sum(g => g.Valor);
        decimal valRech   = retirosGrupos.Where(g => g.Estado == "RECHAZADO").Sum(g => g.Valor);

        return new
        {
            idComercio       = comercio.IdComercio,
            nombreComercial  = comercio.NombreComercial,
            idWalletComercio,
            saldoDisponible,
            ventasQr = new
            {
                total         = vqTotal,
                contingencia  = vqContingencia,
                liquidadas    = vqLiquidadas,
                valorTotal    = vqValor
            },
            liquidaciones = new
            {
                total      = liquidaciones?.Count ?? 0,
                valorTotal = liquidaciones?.Valor ?? 0m
            },
            retiros = new
            {
                total         = retTotal,
                pendientes    = retPendientes,
                pagados       = retPagados,
                rechazados    = retRechazados,
                valorPendiente = valPend,
                valorPagado   = valPagado,
                valorRechazado = valRech
            }
        };
    }

    public async Task<object> GetLedgerTransaccionAsync(long idTransaccion)
    {
        if (idTransaccion <= 0)
            throw new InvalidOperationException("El identificador de transacción debe ser mayor a cero.");

        var tx = await _db.LedgerTransacciones.FirstOrDefaultAsync(t => t.IdTransaccionLedger == idTransaccion)
            ?? throw new InvalidOperationException($"No existe la transacción ledger con id {idTransaccion}.");

        var movimientos = await _db.LedgerMovimientos
            .Where(m => m.IdTransaccionLedger == idTransaccion)
            .OrderBy(m => m.IdMovimientoLedger)
            .Select(m => new
            {
                idMovimiento    = m.IdMovimientoLedger,
                idCuenta        = m.IdCuenta,
                naturaleza      = m.Naturaleza,
                valor           = m.Valor,
                concepto        = m.Concepto,
                descripcion     = m.Descripcion,
                fecha           = m.FechaMovimiento
            })
            .ToListAsync();

        decimal totalDebitos  = movimientos.Where(m => m.naturaleza == "D").Sum(m => m.valor);
        decimal totalCreditos = movimientos.Where(m => m.naturaleza == "C").Sum(m => m.valor);

        return new
        {
            idTransaccion    = tx.IdTransaccionLedger,
            tipoTransaccion  = tx.TipoTransaccion,
            descripcion      = tx.Descripcion,
            valorTotal       = tx.ValorTotal,
            estado           = tx.Estado,
            fecha            = tx.FechaTransaccion,
            totalDebitos,
            totalCreditos,
            balanceado       = totalDebitos == totalCreditos,
            movimientos
        };
    }

    public async Task<object> GetResumenGeneralAsync()
    {
        var totalWallets = await _db.Wallets.CountAsync();

        var saldoUsuarios = await (
            from ws in _db.WalletSaldos
            join w in _db.Wallets on ws.IdWallet equals w.IdWallet
            where w.TipoWallet == "PERSONA"
            select ws.SaldoDisponible
        ).SumAsync();

        var saldoComercios = await (
            from ws in _db.WalletSaldos
            join w in _db.Wallets on ws.IdWallet equals w.IdWallet
            where w.TipoWallet == "COMERCIO"
            select ws.SaldoDisponible
        ).SumAsync();

        var ventasGrupos = await _db.VentasQr
            .GroupBy(v => v.Estado)
            .Select(g => new { Estado = g.Key, Count = g.Count() })
            .ToListAsync();

        int vqTotal        = ventasGrupos.Sum(g => g.Count);
        int vqContingencia = ventasGrupos.Where(g => g.Estado == "CONTINGENCIA").Sum(g => g.Count);
        int vqLiquidadas   = ventasGrupos.Where(g => g.Estado == "LIQUIDADA").Sum(g => g.Count);

        var retirosGrupos = await _db.RetirosComercio
            .GroupBy(r => r.Estado)
            .Select(g => new { Estado = g.Key, Count = g.Count() })
            .ToListAsync();

        int retPendientes = retirosGrupos.Where(g => g.Estado == "PENDIENTE").Sum(g => g.Count);
        int retPagados    = retirosGrupos.Where(g => g.Estado == "PAGADO").Sum(g => g.Count);
        int retRechazados = retirosGrupos.Where(g => g.Estado == "RECHAZADO").Sum(g => g.Count);

        var totalTransacciones = await _db.LedgerTransacciones.CountAsync();
        var totalAuditorias    = await _db.Auditorias.CountAsync();

        return new
        {
            wallets = new
            {
                total         = totalWallets,
                saldoUsuarios,
                saldoComercios
            },
            ventasQr = new
            {
                total        = vqTotal,
                contingencia = vqContingencia,
                liquidadas   = vqLiquidadas
            },
            retiros = new
            {
                pendientes = retPendientes,
                pagados    = retPagados,
                rechazados = retRechazados
            },
            ledger = new
            {
                transacciones = totalTransacciones
            },
            auditoria = new
            {
                eventos = totalAuditorias
            }
        };
    }
}
