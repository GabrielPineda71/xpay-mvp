using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;

namespace Xpay.Api.Authorization;

// P0-B — revocación de JWT. Tras validar firma, emisor, audiencia y vigencia,
// cada request autenticado se contrasta en vivo con usuarios: el usuario debe
// existir, estar ACTIVO y tener version_seguridad igual al claim "vs". Una
// sola consulta por PK, sin caché ni lista de revocados. Cualquier fallo
// funcional responde exactamente "Token no vigente." sin registrar token ni
// claims. Un error de infraestructura (SQL) NO se captura: se propaga como
// fallo técnico y nunca concede acceso. Los tokens anteriores al claim "vs"
// quedan rechazados.
public static class ValidadorVersionSeguridad
{
    public const string MensajeFallo = "Token no vigente.";
    internal const string ClaimIdUsuario = "idUsuario";

    // Encadena cualquier OnTokenValidated previo y no pisa un resultado ya
    // resuelto por él.
    public static void Registrar(JwtBearerOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        opts.Events ??= new JwtBearerEvents();
        var previo = opts.Events.OnTokenValidated;
        opts.Events.OnTokenValidated = async ctx =>
        {
            if (previo is not null) await previo(ctx);
            if (ctx.Result is not null) return;
            await ValidarAsync(ctx);
        };
    }

    internal static async Task ValidarAsync(TokenValidatedContext ctx)
    {
        if (!TryLeerClaims(ctx.Principal, out var idUsuario, out var version))
        {
            ctx.Fail(MensajeFallo);
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<XpayDbContext>();
        if (!await EsVigenteAsync(db, idUsuario, version, ctx.HttpContext.RequestAborted))
            ctx.Fail(MensajeFallo);
    }

    // Exactamente un claim de cada tipo, solo dígitos ASCII (sin signo,
    // espacios ni decimales), sin desbordamiento y en forma decimal canónica.
    internal static bool TryLeerClaims(ClaimsPrincipal? principal, out long idUsuario, out int version)
    {
        idUsuario = 0;
        version   = 0;
        if (principal is null) return false;
        if (!TryValorUnico(principal, ClaimIdUsuario, out var crudoId)
            || !TryValorUnico(principal, VersionSeguridadUsuario.Claim, out var crudoVs))
            return false;

        if (!long.TryParse(crudoId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0
            || !string.Equals(id.ToString(CultureInfo.InvariantCulture), crudoId, StringComparison.Ordinal))
            return false;

        if (!int.TryParse(crudoVs, NumberStyles.None, CultureInfo.InvariantCulture, out var vs)
            || !string.Equals(vs.ToString(CultureInfo.InvariantCulture), crudoVs, StringComparison.Ordinal))
            return false;

        idUsuario = id;
        version   = vs;
        return true;
    }

    // Única consulta: PK + proyección mínima, sin seguimiento.
    internal static async Task<bool> EsVigenteAsync(XpayDbContext db, long idUsuario, int version, CancellationToken ct)
    {
        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new { u.Estado, u.VersionSeguridad })
            .SingleOrDefaultAsync(ct);

        return usuario is not null
            && string.Equals(usuario.Estado, EstadosUsuario.Activo, StringComparison.Ordinal)
            && usuario.VersionSeguridad == version;
    }

    private static bool TryValorUnico(ClaimsPrincipal principal, string tipo, out string valor)
    {
        valor = string.Empty;
        string? encontrado = null;
        foreach (var claim in principal.FindAll(tipo))
        {
            if (encontrado is not null) return false;
            encontrado = claim.Value;
        }
        if (encontrado is null) return false;
        valor = encontrado;
        return true;
    }
}
