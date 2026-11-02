using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

// Resuelve el titular de una wallet destino de transferencia. Lo comparten la
// vista previa (GET /api/wallets/destinatario/{idWallet}) y la propia
// transferencia, para que ambas apliquen exactamente el mismo criterio.
//
// Destino válido: wallet ACTIVA de tipo PERSONA, distinta de la del
// solicitante, cuya persona tiene exactamente un usuario ACTIVO. Cualquier
// otro caso (inexistente, inactiva, de comercio, propia, sin usuario activo o
// con más de uno) devuelve null sin distinguir el motivo.
public class DestinatarioWalletService(XpayDbContext db)
{
    // Vista previa: lectura sin bloqueos.
    public async Task<DestinatarioWalletResponse?> ResolverAsync(long idWallet, long idPersonaSolicitante)
    {
        var wallet = await db.Wallets.AsNoTracking()
            .Where(w => w.IdWallet == idWallet)
            .Select(w => new { w.IdPersona, w.Estado, w.TipoWallet })
            .FirstOrDefaultAsync();
        if (wallet is null || !EsDestinoValido(wallet.IdPersona, wallet.Estado, wallet.TipoWallet, idPersonaSolicitante))
            return null;

        var usuariosActivos = await db.Usuarios.AsNoTracking()
            .Where(u => u.IdPersona == wallet.IdPersona && u.Estado == "ACTIVO")
            .Select(u => u.NombreUsuario)
            .Take(2)
            .ToListAsync();

        return await ConstruirAsync(idWallet, wallet.IdPersona!.Value, usuariosActivos);
    }

    // Transferencia: debe llamarse DENTRO de la transacción de la operación.
    // UPDLOCK + HOLDLOCK retienen hasta el commit/rollback los usuarios de la
    // persona destino (incluido el rango por id_persona, IX_usuarios_persona:
    // impide insertar otro usuario para esa persona) y la fila de la wallet
    // destino. Un cambio concurrente de estado o de titular espera a que la
    // transferencia termine, o la transferencia espera al cambio y lee el
    // valor ya confirmado. AsNoTracking: sin él, EF devolvería la instancia
    // ya rastreada (leída antes, sin bloqueo) en vez de la fila recién
    // bloqueada.
    public async Task<DestinatarioWalletResponse?> ResolverConBloqueoAsync(long idWallet, long idPersonaSolicitante)
    {
        var idPersona = await db.Wallets.AsNoTracking()
            .Where(w => w.IdWallet == idWallet)
            .Select(w => w.IdPersona)
            .FirstOrDefaultAsync();
        if (idPersona is null) return null;

        // Orden fijo: primero usuarios, luego wallet.
        var usuarios = await db.Usuarios
            .FromSqlInterpolated($"SELECT * FROM usuarios WITH (UPDLOCK, HOLDLOCK) WHERE id_persona = {idPersona.Value}")
            .AsNoTracking()
            .ToListAsync();
        var wallet = await db.Wallets
            .FromSqlInterpolated($"SELECT * FROM wallets WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE id_wallet = {idWallet}")
            .AsNoTracking()
            .FirstOrDefaultAsync();

        // La wallet pudo cambiar de persona entre la primera lectura y el bloqueo.
        if (wallet is null || wallet.IdPersona != idPersona
            || !EsDestinoValido(wallet.IdPersona, wallet.Estado, wallet.TipoWallet, idPersonaSolicitante))
            return null;

        var usuariosActivos = usuarios.Where(u => u.Estado == "ACTIVO").Select(u => u.NombreUsuario).Take(2).ToList();
        return await ConstruirAsync(idWallet, idPersona.Value, usuariosActivos);
    }

    private static bool EsDestinoValido(long? idPersona, string estado, string tipoWallet, long idPersonaSolicitante) =>
        idPersona is not null
        && idPersona.Value != idPersonaSolicitante
        && estado == "ACTIVA"
        && tipoWallet == "PERSONA";

    private async Task<DestinatarioWalletResponse?> ConstruirAsync(long idWallet, long idPersona, List<string> usuariosActivos)
    {
        if (usuariosActivos.Count != 1) return null;

        // Solo los dos campos del nombre; nunca documento, celular ni correo.
        var persona = await db.Personas.AsNoTracking()
            .Where(p => p.IdPersona == idPersona)
            .Select(p => new { p.PrimerNombre, p.PrimerApellido })
            .FirstOrDefaultAsync();

        return new DestinatarioWalletResponse(
            idWallet, usuariosActivos[0], EnmascararNombre(persona?.PrimerNombre, persona?.PrimerApellido));
    }

    // "Gabriel P.": primera palabra del primer nombre + inicial del primer
    // apellido. Sin primer nombre → null (se muestra solo el usuario).
    internal static string? EnmascararNombre(string? primerNombre, string? primerApellido)
    {
        var nombre = primerNombre?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(nombre)) return null;
        var apellido = primerApellido?.Trim();
        return string.IsNullOrEmpty(apellido) ? nombre : $"{nombre} {char.ToUpperInvariant(apellido[0])}.";
    }
}
