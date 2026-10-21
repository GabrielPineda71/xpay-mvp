using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// P0-B — tokens de un solo uso (VERIFICAR_EMAIL / RECUPERAR_CLAVE) sobre
// usuario_tokens (migración 048). Solo persiste el SHA-256 del token. Sin
// ILogger a propósito: este servicio no puede registrar token ni hash. No
// envía email y no toca usuario, persona, roles, wallet ni KYC.
public sealed class TokenUsuarioService(
    XpayDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    // Vigencia en minutos: la decide siempre el servidor, nunca el llamador.
    // Un valor configurado fuera de rango aborta la emisión (fail-closed).
    private const string ClaveVerificarEmailMinutos   = "TokensUsuario:VerificarEmailMinutos";
    private const int    VerificarEmailMinutosDefecto = 1440;
    private const int    VerificarEmailMinutosMaximo  = 10080;

    private const string ClaveRecuperarClaveMinutos   = "TokensUsuario:RecuperarClaveMinutos";
    private const int    RecuperarClaveMinutosDefecto = 30;
    private const int    RecuperarClaveMinutosMaximo  = 120;

    private const int    VigenciaMinutosMinimo        = 1;
    private const int    BytesToken                   = 32;
    private const int    LongitudMaximaIp             = 100; // usuario_tokens.ip_solicitud VARCHAR(100)

    public async Task<TokenEmitido> EmitirAsync(
        long idUsuario, string proposito, string emailDestino, string? ipSolicitud,
        CancellationToken ct = default)
    {
        // Validaciones sin tocar BD.
        if (idUsuario <= 0)
            throw new ArgumentOutOfRangeException(nameof(idUsuario));
        if (!PropositoTokenUsuario.EsValido(proposito))
            throw new ArgumentException("Propósito de token no válido.", nameof(proposito));
        if (!EmailNormalizador.TryNormalizar(emailDestino, out var normalizado)
            || !string.Equals(normalizado, emailDestino, StringComparison.Ordinal))
            throw new ArgumentException("El email destino debe llegar ya normalizado.", nameof(emailDestino));

        var vigencia        = Vigencia(proposito);
        var ip              = NormalizarIp(ipSolicitud);
        var ahora           = timeProvider.GetUtcNow().UtcDateTime;
        var fechaExpiracion = ahora.Add(vigencia);

        var bytes = RandomNumberGenerator.GetBytes(BytesToken);
        var token = Base64UrlSinRelleno(bytes);
        var hash  = SHA256.HashData(bytes);

        // Mismo patrón que RegistroUsuarioFinalService/QaIdentidadPruebaStore:
        // se une a la transacción del llamador o abre una propia.
        await using var txPropia = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        UsuarioToken? nuevo = null;
        try
        {
            if (await AppLockHelper.AdquirirAsync(db, ClaveLock(idUsuario, proposito), ct) < 0)
                throw new InvalidOperationException("No se pudo serializar la emisión del token.");

            await db.UsuarioTokens
                .Where(t => t.IdUsuario == idUsuario && t.Proposito == proposito
                         && t.FechaUso == null && t.FechaInvalidacion == null
                         && t.FechaExpiracion > ahora)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.FechaInvalidacion, ahora), ct);

            nuevo = new UsuarioToken
            {
                IdUsuario       = idUsuario,
                Proposito       = proposito,
                TokenHash       = hash,
                EmailDestino    = emailDestino,
                FechaCreacion   = ahora,
                FechaExpiracion = fechaExpiracion,
                IpSolicitud     = ip,
            };
            db.UsuarioTokens.Add(nuevo);
            await db.SaveChangesAsync(ct);

            if (txPropia is not null) await txPropia.CommitAsync(ct);
            return new TokenEmitido(token, fechaExpiracion);
        }
        catch
        {
            if (txPropia is not null)
            {
                // Transacción propia: todo se revierte y el ChangeTracker se
                // limpia (mismo patrón que QaIdentidadPruebaStore).
                await txPropia.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }
            else if (nuevo is not null)
            {
                // Transacción del llamador: ni commit ni rollback ajenos. La
                // transacción queda en estado indeterminado (el ExecuteUpdateAsync
                // pudo haber invalidado filas) y el llamador DEBE revertirla
                // completa. Solo se desengancha la entidad creada aquí, para que
                // un SaveChanges posterior del llamador no la reinserte; no es
                // una recuperación parcial y no se toca ninguna entrada ajena.
                db.Entry(nuevo).State = EntityState.Detached;
            }
            throw;
        }
    }

    public async Task<TokenConsumido?> ConsumirAsync(
        string? token, string proposito, CancellationToken ct = default)
    {
        if (!PropositoTokenUsuario.EsValido(proposito))
            throw new ArgumentException("Propósito de token no válido.", nameof(proposito));

        if (!TryDecodificarTokenCanonico(token, out var bytes))
            return null;

        var hash  = SHA256.HashData(bytes);
        var ahora = timeProvider.GetUtcNow().UtcDateTime;

        var candidato = await db.UsuarioTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == hash && t.Proposito == proposito)
            .Select(t => new { t.IdToken, t.IdUsuario, t.EmailDestino })
            .SingleOrDefaultAsync(ct);

        if (candidato is null) return null;

        var filas = await db.UsuarioTokens
            .Where(t => t.IdToken == candidato.IdToken
                     && t.Proposito == proposito
                     && t.FechaUso == null
                     && t.FechaInvalidacion == null
                     && t.FechaExpiracion > ahora)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.FechaUso, ahora),
                ct);

        if (filas != 1) return null;

        return new TokenConsumido(candidato.IdUsuario, candidato.EmailDestino);
    }

    // El propósito ya llegó validado: si no es VERIFICAR_EMAIL es RECUPERAR_CLAVE.
    // Un valor no numérico también aborta (GetValue lanza InvalidOperationException).
    private TimeSpan Vigencia(string proposito)
    {
        var (clave, defecto, maximo) = proposito == PropositoTokenUsuario.VerificarEmail
            ? (ClaveVerificarEmailMinutos, VerificarEmailMinutosDefecto, VerificarEmailMinutosMaximo)
            : (ClaveRecuperarClaveMinutos, RecuperarClaveMinutosDefecto, RecuperarClaveMinutosMaximo);
        var minutos = configuration.GetValue(clave, defecto);
        if (minutos < VigenciaMinutosMinimo || minutos > maximo)
            throw new InvalidOperationException($"{clave} fuera de rango ({VigenciaMinutosMinimo}..{maximo}).");
        return TimeSpan.FromMinutes(minutos);
    }

    private static string ClaveLock(long idUsuario, string proposito) =>
        $"XPAY:USUARIO_TOKEN:{idUsuario}:{proposito}";

    // Columna VARCHAR no Unicode: solo ASCII imprimible; si no lo es, se descarta (null).
    private static string? NormalizarIp(string? ipSolicitud)
    {
        if (string.IsNullOrWhiteSpace(ipSolicitud)) return null;
        var ip = ipSolicitud.Trim();
        if (ip.Any(c => c < ' ' || c > '~')) return null;
        return ip.Length > LongitudMaximaIp ? ip[..LongitudMaximaIp] : ip;
    }

    // 32 bytes -> 44 caracteres base64 con un '=' -> 43 caracteres base64url sin relleno.
    private static string Base64UrlSinRelleno(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Validación estricta antes de tocar BD: exactamente 43 caracteres
    // base64url (A-Z a-z 0-9 - _), sin '=', espacios ni normalización. La
    // reencodificación final rechaza variantes con bits sobrantes en el último
    // carácter, que Convert aceptaría para los mismos 32 bytes.
    private static bool TryDecodificarTokenCanonico(string? token, out byte[] bytes)
    {
        const int LongitudToken = 43;
        bytes = Array.Empty<byte>();
        if (token is null || token.Length != LongitudToken) return false;
        foreach (var c in token)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                  || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                return false;
        }

        var buffer = new byte[BytesToken];
        var base64 = token.Replace('-', '+').Replace('_', '/') + "=";
        if (!Convert.TryFromBase64String(base64, buffer, out var escritos) || escritos != BytesToken)
            return false;
        if (!string.Equals(Base64UrlSinRelleno(buffer), token, StringComparison.Ordinal))
            return false;

        bytes = buffer;
        return true;
    }
}

// Token en claro: solo para construir el enlace del email. ToString() se
// sobrescribe para que un log o una interpolación accidental nunca lo revele
// (un record imprime todos sus miembros por defecto).
public sealed record TokenEmitido(string Token, DateTime FechaExpiracion)
{
    public override string ToString() =>
        $"TokenEmitido {{ Token = ***, FechaExpiracion = {FechaExpiracion:O} }}";
}

// Resultado de un consumo exitoso. Nunca incluye token ni hash.
public sealed record TokenConsumido(long IdUsuario, string EmailDestino)
{
    public override string ToString() =>
        $"TokenConsumido {{ IdUsuario = {IdUsuario}, EmailDestino = *** }}";
}
