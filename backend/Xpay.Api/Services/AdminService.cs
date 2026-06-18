using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;

namespace Xpay.Api.Services;

public class AdminService
{
    private readonly XpayDbContext _db;
    public AdminService(XpayDbContext db) => _db = db;

    public async Task<object> ListarWalletsAsync(
        string? tipoWallet, string? estado, long? idPersona,
        int page, int pageSize)
    {
        if (page < 1)       page     = 1;
        if (pageSize < 1)   pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.Wallets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(tipoWallet))
            query = query.Where(w => w.TipoWallet == tipoWallet);
        if (!string.IsNullOrWhiteSpace(estado))
            query = query.Where(w => w.Estado == estado);
        if (idPersona.HasValue)
            query = query.Where(w => w.IdPersona == idPersona.Value);

        var total = await query.CountAsync();

        var wallets = await query
            .OrderByDescending(w => w.IdWallet)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var walletIds = wallets.Select(w => w.IdWallet).ToList();
        var saldos = await _db.WalletSaldos
            .Where(s => walletIds.Contains(s.IdWallet))
            .ToDictionaryAsync(s => s.IdWallet, s => s.SaldoDisponible);

        var items = wallets.Select(w => new
        {
            idWallet        = w.IdWallet,
            idPersona       = w.IdPersona,
            idComercio      = w.IdComercio,
            tipoWallet      = w.TipoWallet,
            nombreWallet    = w.NombreWallet,
            estado          = w.Estado,
            saldoDisponible = saldos.TryGetValue(w.IdWallet, out var s) ? s : 0m,
            fechaCreacion   = w.FechaCreacion
        }).ToList();

        return new { items, total, page, pageSize };
    }

    public async Task<object> ListarComerciosAsync(
        string? estado, string? texto,
        int page, int pageSize)
    {
        if (page < 1)       page     = 1;
        if (pageSize < 1)   pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.Comercios.AsQueryable();

        if (!string.IsNullOrWhiteSpace(estado))
            query = query.Where(c => c.Estado == estado);
        if (!string.IsNullOrWhiteSpace(texto))
            query = query.Where(c =>
                c.NombreComercial.Contains(texto) ||
                (c.Nit != null && c.Nit.Contains(texto)));

        var total = await query.CountAsync();

        var comercios = await query
            .OrderByDescending(c => c.IdComercio)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var walletIds = comercios
            .Where(c => c.IdWalletComercio.HasValue)
            .Select(c => c.IdWalletComercio!.Value)
            .ToList();
        var saldos = await _db.WalletSaldos
            .Where(s => walletIds.Contains(s.IdWallet))
            .ToDictionaryAsync(s => s.IdWallet, s => s.SaldoDisponible);

        var items = comercios.Select(c => new
        {
            idComercio       = c.IdComercio,
            nombreComercial  = c.NombreComercial,
            nit              = c.Nit,
            estado           = c.Estado,
            idWalletComercio = c.IdWalletComercio,
            saldoDisponible  = c.IdWalletComercio.HasValue &&
                               saldos.TryGetValue(c.IdWalletComercio.Value, out var s) ? s : 0m
        }).ToList();

        return new { items, total, page, pageSize };
    }

    public async Task<object> ListarVentasQrAsync(
        string? estado, long? idComercio, long? idTienda,
        DateTime? desde, DateTime? hasta,
        int page, int pageSize)
    {
        if (page < 1)       page     = 1;
        if (pageSize < 1)   pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.VentasQr.AsQueryable();

        if (!string.IsNullOrWhiteSpace(estado))
            query = query.Where(v => v.Estado == estado);
        if (idComercio.HasValue)
            query = query.Where(v => v.IdComercio == idComercio.Value);
        if (idTienda.HasValue)
            query = query.Where(v => v.IdTienda == idTienda.Value);
        if (desde.HasValue)
            query = query.Where(v => v.FechaVenta >= desde.Value.Date);
        if (hasta.HasValue)
            query = query.Where(v => v.FechaVenta < hasta.Value.Date.AddDays(1));

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(v => v.FechaVenta)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new
            {
                idVentaQr                = v.IdVentaQr,
                idComercio               = v.IdComercio,
                idTienda                 = v.IdTienda,
                idWalletUsuario          = v.IdWalletUsuario,
                valorBruto               = v.ValorBruto,
                estado                   = v.Estado,
                idTransaccionLedger      = v.IdTransaccionLedger,
                idTransaccionLiquidacion = v.IdTransaccionLiquidacion,
                fechaVenta               = v.FechaVenta
            })
            .ToListAsync();

        return new { items, total, page, pageSize };
    }

    public async Task<object> ListarLedgerTransaccionesAsync(
        string? tipoTransaccion, DateTime? desde, DateTime? hasta,
        int page, int pageSize)
    {
        if (page < 1)       page     = 1;
        if (pageSize < 1)   pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.LedgerTransacciones.AsQueryable();

        if (!string.IsNullOrWhiteSpace(tipoTransaccion))
            query = query.Where(t => t.TipoTransaccion == tipoTransaccion);
        if (desde.HasValue)
            query = query.Where(t => t.FechaTransaccion >= desde.Value.Date);
        if (hasta.HasValue)
            query = query.Where(t => t.FechaTransaccion < hasta.Value.Date.AddDays(1));

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(t => t.FechaTransaccion)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                idTransaccionLedger = t.IdTransaccionLedger,
                tipoTransaccion     = t.TipoTransaccion,
                referenciaTipo      = t.ReferenciaTipo,
                referenciaId        = t.ReferenciaId,
                descripcion         = t.Descripcion,
                valorTotal          = t.ValorTotal,
                fechaTransaccion    = t.FechaTransaccion,
                creadoPor           = t.CreadoPor
            })
            .ToListAsync();

        return new { items, total, page, pageSize };
    }
}
