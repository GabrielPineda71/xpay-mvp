using Xpay.Api.Models;

namespace Xpay.Api.Common;

// P0-B2 — fuente única del bloqueo automático por intentos fallidos.
// AuthService lo escribe al quinto fallo consecutivo y la recuperación de
// contraseña lo reconoce como el ÚNICO bloqueo que el propio usuario puede
// levantar. El literal es el histórico (sin cambios desde el commit inicial):
// las filas ya bloqueadas siguen reconociéndose. La comparación es exacta
// (ordinal, en memoria; la collation SQL es case-insensitive) y exige además
// estado BLOQUEADO y fecha de bloqueo: cualquier otro bloqueo no es elegible.
public static class BloqueoAutomaticoUsuario
{
    public const string Motivo = "Bloqueo automático por intentos fallidos.";

    public static bool Es(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return Es(usuario.Estado, usuario.MotivoBloqueo, usuario.FechaBloqueo);
    }

    public static bool Es(string? estado, string? motivoBloqueo, DateTime? fechaBloqueo) =>
        string.Equals(estado, EstadosUsuario.Bloqueado, StringComparison.Ordinal)
        && string.Equals(motivoBloqueo, Motivo, StringComparison.Ordinal)
        && fechaBloqueo is not null;
}
