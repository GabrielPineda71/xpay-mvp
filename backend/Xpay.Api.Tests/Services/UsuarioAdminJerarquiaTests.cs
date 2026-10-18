using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-A — jerarquía administrativa SUPERUSUARIO > ADMIN_XPAY > resto, derivada
// de los roles persistidos (usuario_roles). Mismo patrón SQL del repositorio:
// se salta en local sin cadena de conexión y es obligatoria en CI.
[Collection("SqlIntegration")]
public sealed class UsuarioAdminJerarquiaTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    public static TheoryData<string, string> Denegados()
    {
        var d = new TheoryData<string, string>();
        foreach (var op in Operaciones)
            foreach (var target in new[] { "ADM2", "ADM_MULTI", "SU1", "SU2" })
                d.Add(op, target);
        return d;
    }

    public static TheoryData<string, string, string> Permitidos()
    {
        var d = new TheoryData<string, string, string>();
        foreach (var op in Operaciones)
        {
            d.Add(op, "ADM1", "U0");
            d.Add(op, "SU1", "ADM2");
            d.Add(op, "SU1", "SU2");
        }
        return d;
    }

    private static readonly string[] Operaciones =
        { "activar", "inactivar", "desbloquear", "reset", "asignar", "revocar" };

    // ADMIN_XPAY nunca administra a otro ADMIN_XPAY (incluido uno con varios
    // roles) ni a un SUPERUSUARIO (incluido uno que además tiene ADMIN_XPAY).
    [Theory]
    [MemberData(nameof(Denegados))]
    public async Task AdminXpay_SobreRangoIgualOSuperior_Denegado(string op, string target)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await PrepararAsync(cs, s, op, s[target]);
            var antes = await LeerUsuarioAsync(cs, s[target]);

            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => EjecutarAsync(new UsuarioAdminService(ctx), op, s, actor: s["ADM1"], target: s[target]));

            var despues = await LeerUsuarioAsync(cs, s[target]);
            Assert.Equal(antes.Estado, despues.Estado);
            Assert.Equal(antes.PasswordHash, despues.PasswordHash); // reset denegado: sin clave nueva
            Assert.Equal(antes.Roles, despues.Roles);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [MemberData(nameof(Permitidos))]
    public async Task RangoSuperior_Permitido(string op, string actor, string target)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await PrepararAsync(cs, s, op, s[target]);
            var antes = await LeerUsuarioAsync(cs, s[target]);

            await using var ctx = NuevoContexto(cs);
            await EjecutarAsync(new UsuarioAdminService(ctx), op, s, actor: s[actor], target: s[target]);

            var despues = await LeerUsuarioAsync(cs, s[target]);
            switch (op)
            {
                case "activar":
                case "desbloquear": Assert.Equal("ACTIVO", despues.Estado); break;
                case "inactivar":   Assert.Equal("INACTIVO", despues.Estado); break;
                case "reset":       Assert.NotEqual(antes.PasswordHash, despues.PasswordHash); break;
                case "asignar":     Assert.Contains("CARTERA_XPAY", despues.Roles); break;
                case "revocar":     Assert.DoesNotContain("CARTERA_XPAY", despues.Roles); break;
            }
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task RestablecerClave_Permitido_DevuelveClaveTemporal_Denegado_NoLaGenera()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var svc = new UsuarioAdminService(ctx);

            var (_, clave) = await svc.RestablecerClaveAsync(s["U0"], s["ADM1"], null);
            Assert.False(string.IsNullOrWhiteSpace(clave));

            string? claveDenegada = null;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                claveDenegada = (await svc.RestablecerClaveAsync(s["ADM2"], s["ADM1"], null)).ClaveTemporal);
            Assert.Null(claveDenegada);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // Protecciones existentes que se conservan.
    [Fact]
    public async Task ProteccionesExistentes_SeConservan()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var svc = new UsuarioAdminService(ctx);

            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.InactivarAsync(s["SU1"], s["SU1"]));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RestablecerClaveAsync(s["ADM1"], s["ADM1"], null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.AsignarRolAsync(s["U0"], s["ADM1"], "SUPERUSUARIO", null));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // Contrato HTTP: la denegación de autoridad es 403, no 500.
    [Fact]
    public async Task Controller_ActivarInactivarDesbloquear_DenegacionEs403()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var c = new UsuariosAdminController(new UsuarioAdminService(ctx),
                new AuditLogService(NullLogger<AuditLogService>.Instance, new ConfigurationBuilder().Build()))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("idUsuario", s["ADM1"].ToString()), new Claim(ClaimTypes.Role, "ADMIN_XPAY"),
                        }, "Test")),
                    },
                },
            };

            Assert.IsType<ForbidResult>(await c.Activar(s["ADM2"]));
            Assert.IsType<ForbidResult>(await c.Inactivar(s["ADM2"]));
            Assert.IsType<ForbidResult>(await c.Desbloquear(s["ADM2"]));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Utilidades ─────────────────────────────────────────────────────────

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
    }

    private sealed record EstadoUsuario(string Estado, string PasswordHash, string Roles);

    private static async Task EjecutarAsync(UsuarioAdminService svc, string op, Dictionary<string, long> s, long actor, long target)
    {
        switch (op)
        {
            case "activar":     await svc.ActivarAsync(target, actor); break;
            case "inactivar":   await svc.InactivarAsync(target, actor); break;
            case "desbloquear": await svc.DesbloquearAsync(target, actor); break;
            case "reset":       await svc.RestablecerClaveAsync(target, actor, null); break;
            case "asignar":     await svc.AsignarRolAsync(target, actor, "CARTERA_XPAY", null); break;
            case "revocar":     await svc.RevocarRolAsync(target, s["ROL_CARTERA"], actor, null); break;
            default: throw new ArgumentOutOfRangeException(nameof(op));
        }
    }

    // Deja el destino en el estado que la operación exige (para que un caso
    // permitido pueda completarse); los denegados fallan antes de mirarlo.
    private static async Task PrepararAsync(string cs, Dictionary<string, long> s, string op, long target)
    {
        await using var ctx = NuevoContexto(cs);
        var u = await ctx.Usuarios.SingleAsync(x => x.IdUsuario == target);
        u.Estado = op switch { "activar" => "INACTIVO", "desbloquear" => "BLOQUEADO", _ => "ACTIVO" };
        if (op == "revocar" && !await ctx.UsuarioRoles.AnyAsync(r => r.IdUsuario == target && r.IdRol == s["ROL_CARTERA"]))
            ctx.UsuarioRoles.Add(new UsuarioRol
            {
                IdUsuario = target, IdRol = s["ROL_CARTERA"], FechaAsignacion = DateTime.UtcNow, Estado = "ACTIVO",
            });
        await ctx.SaveChangesAsync();
    }

    private static async Task<EstadoUsuario> LeerUsuarioAsync(string cs, long idUsuario)
    {
        await using var v = NuevoContexto(cs);
        var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == idUsuario);
        var roles = await (
            from ur in v.UsuarioRoles.AsNoTracking()
            join r in v.Roles.AsNoTracking() on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO"
            orderby r.Codigo
            select r.Codigo).ToListAsync();
        return new EstadoUsuario(u.Estado, u.PasswordHash, string.Join(",", roles));
    }

    // SU1: SUPERUSUARIO. SU2: SUPERUSUARIO + ADMIN_XPAY. ADM1, ADM2: ADMIN_XPAY.
    // ADM_MULTI: ADMIN_XPAY + CARTERA_XPAY. U0: USUARIO_FINAL (rango 0; la
    // protección existente impide dejar a un usuario sin ningún rol).
    private static async Task<Dictionary<string, long>> SembrarAsync(string cs, Sembrados creados)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var roles = await ctx.Roles.AsNoTracking()
            .Where(r => r.Codigo == "SUPERUSUARIO" || r.Codigo == "ADMIN_XPAY" || r.Codigo == "CARTERA_XPAY"
                     || r.Codigo == "USUARIO_FINAL")
            .ToDictionaryAsync(r => r.Codigo, r => r.IdRol);
        var ahora = DateTime.UtcNow;

        async Task<long> UsuarioAsync(params string[] codigos)
        {
            var sufijo = Guid.NewGuid().ToString("N")[..12];
            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
                NumeroDocumento = $"63{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                PrimerNombre = "P0A", PrimerApellido = "Jerarquia", Celular = "3000000000",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"p0aj_{sufijo}",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()), Estado = "ACTIVO", FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            creados.Usuarios.Add(usuario.IdUsuario);

            foreach (var c in codigos)
                ctx.UsuarioRoles.Add(new UsuarioRol
                {
                    IdUsuario = usuario.IdUsuario, IdRol = roles[c], FechaAsignacion = ahora, Estado = "ACTIVO",
                });
            await ctx.SaveChangesAsync();
            return usuario.IdUsuario;
        }

        return new Dictionary<string, long>
        {
            ["SU1"]         = await UsuarioAsync("SUPERUSUARIO"),
            ["SU2"]         = await UsuarioAsync("SUPERUSUARIO", "ADMIN_XPAY"),
            ["ADM1"]        = await UsuarioAsync("ADMIN_XPAY"),
            ["ADM2"]        = await UsuarioAsync("ADMIN_XPAY"),
            ["ADM_MULTI"]   = await UsuarioAsync("ADMIN_XPAY", "CARTERA_XPAY"),
            ["U0"]          = await UsuarioAsync("USUARIO_FINAL"),
            ["ROL_CARTERA"] = roles["CARTERA_XPAY"],
        };
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de jerarquía administrativa.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            var ids = creados.Usuarios;
            await ctx.Auditorias.Where(a => a.IdUsuario != null && ids.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
            await ctx.UsuarioRoles.Where(r => ids.Contains(r.IdUsuario)
                || (r.AsignadoPor != null && ids.Contains(r.AsignadoPor.Value))
                || (r.RevocadoPor != null && ids.Contains(r.RevocadoPor.Value))).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => creados.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
