using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class WalletService
{
    private readonly XpayDbContext _db;
    public WalletService(XpayDbContext db) => _db = db;

    public Task<Wallet?> ObtenerWalletPersonaAsync(long idPersona) => _db.Wallets.FirstOrDefaultAsync(w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA");
    public Task<WalletSaldo?> ObtenerSaldoAsync(long idWallet) => _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == idWallet);
    public Task<List<WalletMovimiento>> ObtenerMovimientosAsync(long idWallet) => _db.WalletMovimientos.Where(m => m.IdWallet == idWallet).OrderByDescending(m => m.FechaMovimiento).Take(100).ToListAsync();
}
