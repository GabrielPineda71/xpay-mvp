using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Protección del último administrador (UsuarioAdminService.InactivarAsync y
// RevocarRolAsync) contra SQL Server local. Desde P0-B (revocación de JWT) el
// caso no es alcanzable por la API: el actor siempre es otro administrador
// activo. Se prueba directamente sobre el servicio, con un actor SUPERUSUARIO
// INACTIVO (su rol sigue activo, así que conserva la autoridad).
//
// Para que el objetivo quede como único administrador activo, el resto de
// administradores de la base se inactiva DENTRO de una transacción que se
// revierte siempre: la guarda falla antes de que el servicio abra su propia
// transacción, y los administradores reales de la base nunca quedan
// modificados.
[Collection("SqlIntegration")]
public sealed class UsuarioAdminUltimoAdminSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string RolSuperusuario = "SUPERUSUARIO";
    private const string RolAdminXpay    = "ADMIN_XPAY";
    private const string RolUsuarioFinal = "USUARIO_FINAL";

    [Fact]
    public async Task Inactivar_UltimoAdministradorActivo_SeRechazaSinCambios()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            var adminsActivosAntes = await AdminsActivosAsync(cs);

            await using (var ctx = NuevoContexto(cs))
            {
                await using var tx = await ctx.Database.BeginTransactionAsync();
                await DejarObjetivoComoUnicoAdminAsync(ctx, s.Objetivo);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => new UsuarioAdminService(ctx).InactivarAsync(s.Objetivo, s.Actor));
                Assert.Contains("último administrador activo", ex.Message);

                await tx.RollbackAsync();
            }

            var objetivo = await FotoAsync(cs, s.Objetivo);
            Assert.Equal(EstadosUsuario.Activo, objetivo.Estado);
            Assert.Equal(0, objetivo.Version);
            Assert.Equal(0, await AuditoriasAsync(cs, s.Actor));
            Assert.Equal(adminsActivosAntes, await AdminsActivosAsync(cs));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task RevocarSuperusuario_UltimoAdministradorActivo_SeRechazaSinCambios()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            var adminsActivosAntes = await AdminsActivosAsync(cs);

            await using (var ctx = NuevoContexto(cs))
            {
                await using var tx = await ctx.Database.BeginTransactionAsync();
                await DejarObjetivoComoUnicoAdminAsync(ctx, s.Objetivo);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => new UsuarioAdminService(ctx).RevocarRolAsync(s.Objetivo, s.IdRolSuperusuario, s.Actor, null));
                Assert.Contains("último administrador activo", ex.Message);

                await tx.RollbackAsync();
            }

            var objetivo = await FotoAsync(cs, s.Objetivo);
            Assert.Contains(RolSuperusuario, objetivo.Roles);
            Assert.Equal(0, objetivo.Version);
            Assert.Equal(0, await AuditoriasAsync(cs, s.Actor));
            Assert.Equal(adminsActivosAntes, await AdminsActivosAsync(cs));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private sealed record Sembrado(long Actor, long Objetivo, long IdRolSuperusuario);

    private sealed record Foto(string Estado, int Version, string Roles);

    // Inactiva, dentro de la transacción del contexto, a todo administrador
    // activo distinto del objetivo (incluido el actor) y comprueba que el
    // objetivo es el único que queda.
    private static async Task DejarObjetivoComoUnicoAdminAsync(XpayDbContext ctx, long idObjetivo)
    {
        Assert.NotNull(ctx.Database.CurrentTransaction);

        await ctx.Usuarios
            .Where(u => u.IdUsuario != idObjetivo && u.Estado == EstadosUsuario.Activo
                && ctx.UsuarioRoles.Any(ur => ur.IdUsuario == u.IdUsuario && ur.Estado == "ACTIVO"
                    && ctx.Roles.Any(r => r.IdRol == ur.IdRol
                        && (r.Codigo == RolAdminXpay || r.Codigo == RolSuperusuario))))
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.Estado, EstadosUsuario.Inactivo));

        Assert.Equal(1, await AdminsActivosAsync(ctx));
    }

    private static async Task<int> AdminsActivosAsync(string cs)
    {
        await using var ctx = NuevoContexto(cs);
        return await AdminsActivosAsync(ctx);
    }

    private static Task<int> AdminsActivosAsync(XpayDbContext ctx) => (
        from u in ctx.Usuarios
        join ur in ctx.UsuarioRoles on u.IdUsuario equals ur.IdUsuario
        join r in ctx.Roles on ur.IdRol equals r.IdRol
        where u.Estado == EstadosUsuario.Activo && ur.Estado == "ACTIVO"
           && (r.Codigo == RolAdminXpay || r.Codigo == RolSuperusuario)
        select u.IdUsuario
    ).Distinct().CountAsync();

    private static async Task<Foto> FotoAsync(string cs, long idUsuario)
    {
        await using var ctx = NuevoContexto(cs);
        var u = await ctx.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == idUsuario);
        var roles = await (
            from ur in ctx.UsuarioRoles.AsNoTracking()
            join r in ctx.Roles.AsNoTracking() on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO"
            orderby r.Codigo
            select r.Codigo).ToListAsync();
        return new Foto(u.Estado, u.VersionSeguridad, string.Join(",", roles));
    }

    private static async Task<int> AuditoriasAsync(string cs, long idActor)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Auditorias.AsNoTracking().CountAsync(a => a.IdUsuario == idActor);
    }

    // Actor SUPERUSUARIO INACTIVO; objetivo ACTIVO con SUPERUSUARIO y
    // USUARIO_FINAL (así la única razón de rechazo es la guarda).
    private static async Task<Sembrado> SembrarAsync(string cs, Sembrados creados)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        async Task<long> IdRolAsync(string codigo) =>
            await ctx.Roles.Where(r => r.Codigo == codigo).Select(r => r.IdRol).SingleAsync();
        var ahora = DateTime.UtcNow;

        async Task<long> UsuarioAsync(string prefijo, string estado, params string[] roles)
        {
            var sufijo = Guid.NewGuid().ToString("N")[..12];
            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"UAD{sufijo}",
                PrimerNombre = "UAD", PrimerApellido = "Sintetico", Celular = "3000000000",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"{prefijo}_{sufijo}",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                Estado = estado, FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            creados.Usuarios.Add(usuario.IdUsuario);

            foreach (var codigo in roles)
                ctx.UsuarioRoles.Add(new UsuarioRol
                {
                    IdUsuario = usuario.IdUsuario, IdRol = await IdRolAsync(codigo),
                    Estado = "ACTIVO", FechaAsignacion = ahora,
                });
            await ctx.SaveChangesAsync();
            return usuario.IdUsuario;
        }

        var actor    = await UsuarioAsync("uad_act", EstadosUsuario.Inactivo, RolSuperusuario);
        var objetivo = await UsuarioAsync("uad_obj", EstadosUsuario.Activo, RolSuperusuario, RolUsuarioFinal);
        return new Sembrado(actor, objetivo, await IdRolAsync(RolSuperusuario));
    }

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL del último administrador.");
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
