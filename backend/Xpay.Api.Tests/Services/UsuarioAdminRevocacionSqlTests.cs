using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — revocación atómica de version_seguridad en UsuarioAdminService
// (activar, inactivar, desbloquear, restablecer clave, asignar y revocar rol)
// contra SQL Server local. Siempre se revoca al usuario OBJETIVO, nunca al
// administrador. Datos sintéticos propios y limpieza best-effort en finally
// (auditoria y usuario_roles antes que usuario/persona por FK).
[Collection("SqlIntegration")]
public sealed class UsuarioAdminRevocacionSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string RolCartera    = "CARTERA_XPAY";

    public static TheoryData<string> Operaciones() => new()
    {
        "activar", "inactivar", "desbloquear", "reset", "asignar", "revocar",
    };

    // ── Éxito: revoca al objetivo, no al actor ──────────────────────────

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task Exito_RevocaAlObjetivo_NoAlActor_YAplicaElCambio(string op)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, op, EstadoParaExito(op), rolCarteraObjetivo: op == "revocar");
            var antes = await FotoAsync(cs, s.Objetivo);

            await using (var ctx = NuevoContexto(cs))
                await EjecutarAsync(new UsuarioAdminService(ctx), op, s);

            var despues = await FotoAsync(cs, s.Objetivo);
            Assert.Equal(1, despues.Version);
            Assert.Equal(0, (await FotoAsync(cs, s.Actor)).Version);
            Assert.False(await VigenteAsync(cs, s.Objetivo, 0));
            Assert.Equal(1, await AuditoriasAsync(cs, s.Actor));
            Assert.Equal(1, await AuditoriasAsync(cs, s.Actor, AccionEsperada(op)));

            switch (op)
            {
                case "activar":     Assert.Equal(EstadosUsuario.Activo, despues.Estado); break;
                case "inactivar":   Assert.Equal(EstadosUsuario.Inactivo, despues.Estado); break;
                case "desbloquear":
                    Assert.Equal(EstadosUsuario.Activo, despues.Estado);
                    Assert.Equal(0, despues.IntentosFallidos);
                    Assert.Null(despues.FechaBloqueo);
                    break;
                case "reset":
                    Assert.NotEqual(antes.PasswordHash, despues.PasswordHash);
                    Assert.True(despues.RequiereCambioClave);
                    break;
                case "asignar":     Assert.Contains(RolCartera, despues.Roles); break;
                case "revocar":     Assert.DoesNotContain(RolCartera, despues.Roles); break;
            }
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Validación fallida: sin cambios ni revocación ───────────────────

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task ValidacionFallida_NoRevoca_NiCambiaNada(string op)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            // Estado o rol que hace fallar la transición antes de modificar nada.
            var s = await SembrarAsync(cs, creados, op, EstadoParaValidacionFallida(op), rolCarteraObjetivo: op == "asignar");
            var antes = await FotoAsync(cs, s.Objetivo);

            await using (var ctx = NuevoContexto(cs))
                await Assert.ThrowsAsync<TransicionUsuarioInvalidaException>(
                    () => EjecutarAsync(new UsuarioAdminService(ctx), op, s));

            Assert.Equal(antes, await FotoAsync(cs, s.Objetivo));
            Assert.Equal(0, (await FotoAsync(cs, s.Objetivo)).Version);
            Assert.Equal(0, (await FotoAsync(cs, s.Actor)).Version);
            Assert.Equal(0, await AuditoriasAsync(cs, s.Actor));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Falta de autoridad (ADMIN_XPAY sobre SUPERUSUARIO): sin revocación ─

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task SinAutoridad_NoRevoca_NiCambiaNada(string op)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, op, EstadoParaExito(op), rolCarteraObjetivo: op == "revocar",
                rolActor: "ADMIN_XPAY", rolBaseObjetivo: "SUPERUSUARIO");
            var antes = await FotoAsync(cs, s.Objetivo);

            await using (var ctx = NuevoContexto(cs))
                await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => EjecutarAsync(new UsuarioAdminService(ctx), op, s));

            Assert.Equal(antes, await FotoAsync(cs, s.Objetivo));
            Assert.Equal(0, (await FotoAsync(cs, s.Actor)).Version);
            Assert.Equal(0, await AuditoriasAsync(cs, s.Actor));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Rollback: si falla el incremento no persiste nada ───────────────

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task FallaElIncremento_RevierteTodo(string op)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, op, EstadoParaExito(op), rolCarteraObjetivo: op == "revocar");
            var antes = await FotoAsync(cs, s.Objetivo);

            await using (var ctx = ContextoQueFallaAlIncrementar(cs))
                await AssertFalloInyectadoAsync(() => EjecutarAsync(new UsuarioAdminService(ctx), op, s));

            Assert.Equal(antes, await FotoAsync(cs, s.Objetivo));
            Assert.Equal(0, (await FotoAsync(cs, s.Actor)).Version);
            Assert.Equal(0, await AuditoriasAsync(cs, s.Actor));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private sealed record Sembrado(long Actor, long Objetivo, long IdRolCartera);

    private sealed record Foto(
        string Estado, string PasswordHash, bool RequiereCambioClave, int IntentosFallidos,
        DateTime? FechaBloqueo, int Version, string Roles);

    private static string EstadoParaExito(string op) => op switch
    {
        "activar"     => EstadosUsuario.Inactivo,
        "desbloquear" => EstadosUsuario.Bloqueado,
        _             => EstadosUsuario.Activo,
    };

    private static string EstadoParaValidacionFallida(string op) => op switch
    {
        "inactivar" => EstadosUsuario.Inactivo,
        "reset"     => EstadosUsuario.Inactivo,
        _           => EstadosUsuario.Activo, // activar/desbloquear sobre ACTIVO; asignar/revocar por rol
    };

    private static string AccionEsperada(string op) => op switch
    {
        "activar"     => "USUARIO_ACTIVAR",
        "inactivar"   => "USUARIO_INACTIVAR",
        "desbloquear" => "USUARIO_DESBLOQUEAR",
        "reset"       => "USUARIO_RESTABLECER_CLAVE",
        "asignar"     => "USUARIO_ROL_ASIGNAR",
        "revocar"     => "USUARIO_ROL_REVOCAR",
        _             => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static Task EjecutarAsync(UsuarioAdminService svc, string op, Sembrado s) => op switch
    {
        "activar"     => svc.ActivarAsync(s.Objetivo, s.Actor),
        "inactivar"   => svc.InactivarAsync(s.Objetivo, s.Actor),
        "desbloquear" => svc.DesbloquearAsync(s.Objetivo, s.Actor),
        "reset"       => svc.RestablecerClaveAsync(s.Objetivo, s.Actor, null),
        "asignar"     => svc.AsignarRolAsync(s.Objetivo, s.Actor, RolCartera, null),
        "revocar"     => svc.RevocarRolAsync(s.Objetivo, s.IdRolCartera, s.Actor, null),
        _             => throw new ArgumentOutOfRangeException(nameof(op)),
    };

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
        return new Foto(u.Estado, u.PasswordHash, u.RequiereCambioClave, u.IntentosFallidos,
            u.FechaBloqueo, u.VersionSeguridad, string.Join(",", roles));
    }

    private static async Task<int> AuditoriasAsync(string cs, long idActor, string? accion = null)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Auditorias.AsNoTracking()
            .CountAsync(a => a.IdUsuario == idActor && (accion == null || a.Accion == accion));
    }

    private static async Task<bool> VigenteAsync(string cs, long id, int version)
    {
        await using var ctx = NuevoContexto(cs);
        return await ValidadorVersionSeguridad.EsVigenteAsync(ctx, id, version, CancellationToken.None);
    }

    // El incremento atómico es un UPDATE … version_seguridad + 1: el
    // interceptor lo hace fallar para probar que nada de la operación persiste.
    private static XpayDbContext ContextoQueFallaAlIncrementar(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs)
            .AddInterceptors(new FallaEnIncremento()).Options);

    private sealed class FalloInyectadoException : Exception
    {
        public FalloInyectadoException() : base("Fallo inyectado en el incremento de version_seguridad.") { }
    }

    private sealed class FallaEnIncremento : DbCommandInterceptor
    {
        private static bool EsIncremento(DbCommand c)
            => c.CommandText.Contains("[version_seguridad] + 1", StringComparison.Ordinal);

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            => EsIncremento(command) ? throw new FalloInyectadoException() : base.NonQueryExecuting(command, eventData, result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => EsIncremento(command) ? throw new FalloInyectadoException()
                                     : base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static async Task AssertFalloInyectadoAsync(Func<Task> accion)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(accion);
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is FalloInyectadoException) return;
        Assert.Fail("Se esperaba el fallo inyectado en el incremento.");
    }

    // Actor con rolActor (SUPERUSUARIO por defecto). Objetivo con estado dado,
    // rol base (USUARIO_FINAL por defecto) y opcionalmente CARTERA_XPAY activo.
    private static async Task<Sembrado> SembrarAsync(
        string cs, Sembrados creados, string op, string estadoObjetivo, bool rolCarteraObjetivo,
        string rolActor = "SUPERUSUARIO", string rolBaseObjetivo = "USUARIO_FINAL")
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        async Task<long> IdRolAsync(string codigo) =>
            await ctx.Roles.Where(r => r.Codigo == codigo).Select(r => r.IdRol).SingleAsync();
        var idRolCartera = await IdRolAsync(RolCartera);
        var ahora = DateTime.UtcNow;

        async Task<long> UsuarioAsync(string prefijo, string estado, params string[] roles)
        {
            var sufijo = Guid.NewGuid().ToString("N")[..12];
            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"P0U{sufijo}",
                PrimerNombre = "P0U", PrimerApellido = "Sintetico", Celular = "3000000000",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var bloqueado = estado == EstadosUsuario.Bloqueado;
            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"{prefijo}_{sufijo}",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                Estado = estado, FechaCreacion = ahora,
                IntentosFallidos = bloqueado ? 5 : 0,
                FechaBloqueo     = bloqueado ? ahora : null,
                MotivoBloqueo    = bloqueado ? "Bloqueo automático por intentos fallidos." : null,
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

        var actor = await UsuarioAsync("p0u_act", EstadosUsuario.Activo, rolActor);
        var rolesObjetivo = rolCarteraObjetivo ? new[] { rolBaseObjetivo, RolCartera } : new[] { rolBaseObjetivo };
        var objetivo = await UsuarioAsync("p0u_obj", estadoObjetivo, rolesObjetivo);
        return new Sembrado(actor, objetivo, idRolCartera);
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
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B revocación admin.");
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
