using System.Net.Mail;

namespace Xpay.Api.Common;

// P0-B — normalización única de email (registro, verificación, recuperación).
// Trim + minúsculas invariantes. Sin reglas de proveedor: no quita puntos, no
// quita +alias, no reescribe dominios. Solo ASCII imprimible sin espacios:
// personas.email y usuario_tokens.email_destino son VARCHAR(200) no Unicode,
// así que cualquier otro carácter podría perderse al persistir. No registra
// nada en logs y no envía email.
public static class EmailNormalizador
{
    public const int LongitudMaxima      = 200;
    public const int LongitudMaximaLocal = 64;

    public static bool TryNormalizar(string? entrada, out string email)
    {
        email = string.Empty;
        if (string.IsNullOrWhiteSpace(entrada)) return false;

        var e = entrada.Trim().ToLowerInvariant();
        if (e.Length > LongitudMaxima) return false;
        if (e.Any(c => c <= ' ' || c > '~')) return false;

        var arroba = e.IndexOf('@');
        if (arroba <= 0 || arroba != e.LastIndexOf('@') || arroba > LongitudMaximaLocal) return false;

        var dominio = e[(arroba + 1)..];
        if (dominio.Length < 3 || !dominio.Contains('.') || dominio.StartsWith('.')
            || dominio.EndsWith('.') || dominio.Contains("..")) return false;

        try
        {
            // Rechaza nombre visible o listas ("a <b@c.co>", "a@b.co,c@d.co").
            if (!string.Equals(new MailAddress(e).Address, e, StringComparison.Ordinal)) return false;
        }
        catch (FormatException) { return false; }

        email = e;
        return true;
    }
}
