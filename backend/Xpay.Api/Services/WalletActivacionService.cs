using Microsoft.EntityFrameworkCore;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public sealed record EstadoActivacionWallet(
    bool WalletActiva, bool IdentidadVerificada, bool EmailVerificado, bool WhatsAppVerificado,
    bool WhatsAppDisponible, bool WalletRestringida)
{
    public bool PuedeActivar => !WalletActiva && !WalletRestringida && IdentidadVerificada
        && EmailVerificado && WhatsAppVerificado;
}

public sealed class WalletActivacionService(XpayDbContext db, ContactoVerificacionService verificacion)
{
    internal const string PendienteVerificacion = "PENDIENTE_VERIFICACION";

    public async Task<EstadoActivacionWallet?> ObtenerEstadoAsync(
        long idUsuario, long idPersona, CancellationToken ct)
    {
        var usuario = await CargarUsuarioAsync(idUsuario, idPersona, bloquear: false, ct);
        if (usuario is null) return null;
        var wallets = await WalletsPersona(idPersona).AsNoTracking().ToListAsync(ct);
        return await EstadoAsync(usuario, wallets, ct);
    }

    public async Task<EstadoActivacionWallet?> ActivarAsync(long idUsuario, long idPersona, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Reutilizamos los locks que protegen la decisión de Veriff y los
        // cambios/verificaciones de contacto antes de releer las condiciones.
        if (await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{idUsuario}", ct) < 0)
            throw new InvalidOperationException("Hay una verificación de identidad en curso. Intenta de nuevo.");
        await ContactoVerificacionService.BloquearPersonaAsync(db, idPersona, ct);

        var usuario = await CargarUsuarioAsync(idUsuario, idPersona, bloquear: true, ct);
        if (usuario is null) return null;
        var wallets = await WalletsPersona(idPersona).ToListAsync(ct);
        var estado = await EstadoAsync(usuario, wallets, ct);
        if (!estado.PuedeActivar) return estado;

        var ahora = DateTime.UtcNow;
        var wallet = wallets.SingleOrDefault();
        if (wallet is null)
        {
            var unidad = await db.Personas.Where(p => p.IdPersona == idPersona)
                .Select(p => p.IdUnidadNegocio).SingleAsync(ct);
            wallet = new Wallet
            {
                IdUnidadNegocio = unidad,
                IdPersona = idPersona,
                TipoWallet = "PERSONA",
                NombreWallet = $"Wallet {usuario.NombreUsuario}",
                FechaCreacion = ahora,
            };
            db.Wallets.Add(wallet);
        }

        wallet.Estado = "ACTIVA";
        wallet.FechaActualizacion = ahora;
        await db.SaveChangesAsync(ct);
        if (!await db.WalletSaldos.AnyAsync(s => s.IdWallet == wallet.IdWallet, ct))
            db.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = ahora });
        db.Auditorias.Add(new Auditoria
        {
            IdUsuario = idUsuario,
            IdPersona = idPersona,
            Modulo = "WALLET",
            Accion = "ACTIVAR_WALLET_PERSONA",
            Entidad = "wallets",
            IdEntidad = wallet.IdWallet.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Resultado = "EXITOSO",
            FechaEvento = ahora,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return estado with { WalletActiva = true };
    }

    private Task<Usuario?> CargarUsuarioAsync(long idUsuario, long idPersona, bool bloquear, CancellationToken ct)
    {
        var usuarios = bloquear
            ? db.Usuarios.FromSqlInterpolated($"SELECT * FROM usuarios WITH (UPDLOCK, ROWLOCK) WHERE id_usuario = {idUsuario}")
            : db.Usuarios.AsQueryable();
        return usuarios.AsNoTracking().SingleOrDefaultAsync(
            u => u.IdUsuario == idUsuario && u.IdPersona == idPersona && u.Estado == EstadosUsuario.Activo, ct);
    }

    private IQueryable<Wallet> WalletsPersona(long idPersona) =>
        db.Wallets.Where(w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA");

    private async Task<EstadoActivacionWallet> EstadoAsync(Usuario usuario, List<Wallet> wallets, CancellationToken ct)
    {
        var activa = wallets.Any(w => w.Estado == "ACTIVA");
        var veriff = await KycAprobadoAuthorizationHandler.CargarEvidenciaAsync(
            db, usuario.IdUsuario, incluirSesionVigente: true, ct);
        return new EstadoActivacionWallet(
            activa, veriff is not null && KycAprobadoRegla.CumpleVeriff(veriff),
            usuario.EmailVerificado, usuario.CelularVerificado,
            verificacion.CanalDisponible(CanalContacto.WhatsApp),
            !activa && (wallets.Count > 1 || wallets.Any(w => w.Estado != PendienteVerificacion)));
    }
}
