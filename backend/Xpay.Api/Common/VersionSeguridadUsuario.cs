using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;

namespace Xpay.Api.Common;

// P0-B — revocación de JWT por usuarios.version_seguridad (migración 049).
// El JWT llevará el claim "vs"; toda operación sensible (contraseña, estado,
// roles) llama a IncrementarAsync dentro de SU transacción. El incremento es
// un UPDATE atómico (version_seguridad = version_seguridad + 1) serializado
// por el bloqueo de fila: nunca pierde un incremento concurrente, a
// diferencia de un read-modify-write en memoria. No hace commit ni rollback,
// no relee la versión y no actualiza una entidad Usuario ya rastreada.
public static class VersionSeguridadUsuario
{
    public const string Claim = "vs";

    public static async Task IncrementarAsync(XpayDbContext db, long idUsuario, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (idUsuario <= 0)
            throw new ArgumentOutOfRangeException(nameof(idUsuario));
        if (db.Database.CurrentTransaction is null)
            throw new VersionSeguridadException(
                "El incremento de la versión de seguridad requiere la transacción de la operación sensible.");

        var filas = await db.Usuarios
            .Where(u => u.IdUsuario == idUsuario)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.VersionSeguridad, u => u.VersionSeguridad + 1), ct);

        if (filas != 1)
            throw new VersionSeguridadException("No se pudo incrementar la versión de seguridad del usuario.");
    }
}
