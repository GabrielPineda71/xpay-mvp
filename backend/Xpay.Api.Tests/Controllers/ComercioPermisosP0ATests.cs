using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// P0-A — aislamiento y permisos financieros de comercio. Mismo patrón SQL que
// ComercioScopeService*Tests: se salta en local sin cadena de conexión y es
// obligatoria en CI. Los casos DENEGADOS usan dependencias null: si la acción
// las alcanzara, su catch genérico daría 500, así que un ForbidResult prueba
// que la denegación ocurre antes de cualquier operación. Los casos PERMITIDOS
// usan servicios reales con una petición inválida posterior a la autorización
// (400 determinista), sin escritura ni llamada a Passport.
[Collection("SqlIntegration")]
public sealed class ComercioPermisosP0ATests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // ── 1. ComercioDisponibilidad: aislamiento por comercio ───────────────

    [Fact]
    public async Task Disponibilidad_ComercioAjeno_403EnLasTresAcciones_SinTocarElServicio()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var c = Disponibilidad(ctx, s.AdminComercioA, disp: null);

            Assert.IsType<ForbidResult>(await c.GetResumen(s.ComercioB));
            Assert.IsType<ForbidResult>(await c.ListarNoDisponibles(s.ComercioB));
            Assert.IsType<ForbidResult>(await c.LiquidarAhora(1, s.ComercioB));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Disponibilidad_ComercioPropio_ConservaComportamiento()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var c = Disponibilidad(ctx, s.AdminComercioA,
                new ComercioDisponibilidadService(ctx, NullLogger<ComercioDisponibilidadService>.Instance));

            Assert.IsType<OkObjectResult>(await c.GetResumen(s.ComercioA));
            Assert.IsType<OkObjectResult>(await c.ListarNoDisponibles(s.ComercioA));
            // Disponibilidad inexistente del propio comercio: superó el scope y
            // el servicio responde 404, sin ningún efecto.
            Assert.IsType<NotFoundObjectResult>(await c.LiquidarAhora(long.MaxValue, s.ComercioA));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("CAJERO")]
    [InlineData("ADMIN_SEDE_COMERCIO")]
    public async Task Disponibilidad_SinPuedeLiquidarAnticipado_SigueDenegado(string rol)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var c = Disponibilidad(ctx, s.UsuarioDe(rol), disp: null);

            Assert.IsType<ForbidResult>(await c.GetResumen(s.ComercioA));
            Assert.IsType<ForbidResult>(await c.ListarNoDisponibles(s.ComercioA));
            Assert.IsType<ForbidResult>(await c.LiquidarAhora(1, s.ComercioA));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 2. Bre-B de comercio: PuedeEnviarBreb ─────────────────────────────

    [Theory]
    [InlineData("CAJERO")]
    [InlineData("ADMIN_SEDE_COMERCIO")]
    public async Task BrebComercio_SinPuedeEnviarBreb_403EnLlaveYSimulacion(string rol)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var c = Breb(ctx, PrincipalComercio(s.UsuarioDe(rol)), breb: null);

            Assert.IsType<ForbidResult>(await c.RegistrarLlaveComercio(LlaveInvalida(s.ComercioA)));
            Assert.IsType<ForbidResult>(await c.SimularRetiroComercio(new SimularRetiroRequest { IdComercio = s.ComercioA, Valor = 0 }));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task BrebComercio_ComercioAjeno_403()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var c = Breb(ctx, PrincipalComercio(s.AdminComercioA), breb: null);

            Assert.IsType<ForbidResult>(await c.RegistrarLlaveComercio(LlaveInvalida(s.ComercioB)));
            Assert.IsType<ForbidResult>(await c.SimularRetiroComercio(new SimularRetiroRequest { IdComercio = s.ComercioB, Valor = 0 }));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // Permitidos: la petición inválida falla en la validación del servicio
    // real (antes de BD y de Passport) con 400 — prueba que superó la
    // autorización. No se interpreta como éxito funcional del registro.
    [Fact]
    public async Task BrebComercio_AdminComercioPropio_SuperaLaAutorizacion()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var c = Breb(ctx, PrincipalComercio(s.AdminComercioA), BrebReal(ctx));

            AssertBadRequest(await c.RegistrarLlaveComercio(LlaveInvalida(s.ComercioA)), "Tipo de llave inválido");
            AssertBadRequest(await c.SimularRetiroComercio(new SimularRetiroRequest { IdComercio = s.ComercioA, Valor = 0 }),
                "El valor del retiro debe ser mayor a cero");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task BrebComercio_AdminXpay_ConservaBypassAdministrativo()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            // ADMIN_XPAY sin fila en comercio_usuarios ni rol COMERCIO.
            var admin = Principal(s.AdminComercioA, "ADMIN_XPAY");
            var c = Breb(ctx, admin, BrebReal(ctx));

            AssertBadRequest(await c.RegistrarLlaveComercio(LlaveInvalida(s.ComercioB)), "Tipo de llave inválido");
            AssertBadRequest(await c.SimularRetiroComercio(new SimularRetiroRequest { IdComercio = s.ComercioB, Valor = 0 }),
                "El valor del retiro debe ser mayor a cero");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── 3. Retiro de comercio: PuedeDisponerRecursos ───────────────────────

    [Theory]
    [InlineData("CAJERO")]
    [InlineData("ADMIN_SEDE_COMERCIO")]
    public async Task Retiro_SinPuedeDisponerRecursos_DenegadoSinCrearNada(string rol)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var svc = Retiro(ctx);
            var req = new SolicitarRetiroComercioRequest { IdComercio = s.ComercioA, Valor = 1_000m };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.SolicitarRetiroAsync(req, s.UsuarioDe(rol), esAdministrativo: false));

            await using var v = NuevoContexto(cs);
            Assert.Equal(0, await v.RetirosComercio.AsNoTracking().CountAsync(r => r.IdComercio == s.ComercioA));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // Permitido: Valor=0 falla en la validación posterior al permiso, y el
    // IdComercio ajeno recibido queda forzado al del scope (aislamiento).
    [Fact]
    public async Task Retiro_AdminComercio_SuperaPermisoYConservaAislamiento()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var svc = Retiro(ctx);
            var req = new SolicitarRetiroComercioRequest { IdComercio = s.ComercioB, Valor = 0m };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.SolicitarRetiroAsync(req, s.AdminComercioA, esAdministrativo: false));
            Assert.Contains("valor del retiro", ex.Message);
            Assert.Equal(s.ComercioA, req.IdComercio);

            await using var v = NuevoContexto(cs);
            Assert.Equal(0, await v.RetirosComercio.AsNoTracking()
                .CountAsync(r => r.IdComercio == s.ComercioA || r.IdComercio == s.ComercioB));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Utilidades ─────────────────────────────────────────────────────────

    private sealed record Sembrado(long ComercioA, long ComercioB, long AdminComercioA, long AdminSedeA, long CajeroA)
    {
        public long UsuarioDe(string rol) => rol switch
        {
            "CAJERO"              => CajeroA,
            "ADMIN_SEDE_COMERCIO" => AdminSedeA,
            _                     => AdminComercioA,
        };
    }

    private sealed class Sembrados
    {
        public List<long> Personas  { get; } = new();
        public List<long> Usuarios  { get; } = new();
        public List<long> Aliados   { get; } = new();
        public List<long> Comercios { get; } = new();
    }

    private static ClaimsPrincipal Principal(long idUsuario, params string[] roles)
    {
        var claims = new List<Claim> { new("idUsuario", idUsuario.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal PrincipalComercio(long idUsuario) => Principal(idUsuario, "COMERCIO");

    private static ComercioDisponibilidadController Disponibilidad(
        XpayDbContext ctx, long idUsuario, ComercioDisponibilidadService? disp)
        => new(disp!, new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = PrincipalComercio(idUsuario) } },
        };

    private static BrebController Breb(XpayDbContext ctx, ClaimsPrincipal user, BrebService? breb)
        => new(breb!, new AuditLogService(NullLogger<AuditLogService>.Instance, new ConfigurationBuilder().Build()),
               new ConfigurationBuilder().Build(), new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance),
               null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };

    // Cliente de llaves null: los caminos ejercitados fallan en la validación
    // de entrada antes de cualquier uso del cliente Passport.
    private static BrebService BrebReal(XpayDbContext ctx)
        => new(ctx, NullLogger<BrebService>.Instance, null!, new ConfigurationBuilder().Build());

    private static RetiroComercioService Retiro(XpayDbContext ctx)
        => new(ctx, new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance));

    private static RegistrarLlaveRequest LlaveInvalida(long idComercio)
        => new() { IdComercio = idComercio, KeyType = "TIPO_INVALIDO_P0A", KeyValue = "x" };

    private static void AssertBadRequest(IActionResult r, string fragmentoMensaje)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(r);
        Assert.Contains(fragmentoMensaje, bad.Value!.ToString());
    }

    private static async Task<Sembrado> SembrarAsync(string cs, Sembrados creados)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var ahora = DateTime.UtcNow;

        async Task<(long Comercio, long Aliado)> ComercioAsync()
        {
            var sufijo = Guid.NewGuid().ToString("N")[..10];
            var comercio = new Comercio
            {
                IdUnidadNegocio = idUnidad, NombreComercial = $"P0A {sufijo}", Estado = "ACTIVO", FechaCreacion = ahora,
            };
            ctx.Comercios.Add(comercio);
            await ctx.SaveChangesAsync();
            creados.Comercios.Add(comercio.IdComercio);

            var aliado = new ComercioAliado
            {
                IdComercioExistente = comercio.IdComercio, RazonSocial = $"P0A {sufijo}", NombreComercial = $"P0A {sufijo}",
                Nit = $"8{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}", TipoPersona = "JURIDICA",
                Estado = "ACTIVO", FechaSolicitud = ahora, CreatedAt = ahora,
            };
            ctx.ComerciosAliados.Add(aliado);
            await ctx.SaveChangesAsync();
            creados.Aliados.Add(aliado.IdComercioAliado);
            return (comercio.IdComercio, aliado.IdComercioAliado);
        }

        async Task<long> UsuarioAsync(long idAliado, long idComercio, string rol)
        {
            var sufijo = Guid.NewGuid().ToString("N")[..12];
            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
                NumeroDocumento = $"64{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                PrimerNombre = "P0A", PrimerApellido = "Sintetico", Celular = "3000000000",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"p0a_{sufijo}",
                PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            creados.Usuarios.Add(usuario.IdUsuario);

            ctx.ComercioUsuarios.Add(new ComercioUsuario
            {
                IdComercioAliado = idAliado, IdComercioExistente = idComercio, IdUsuario = usuario.IdUsuario,
                RolComercio = rol, Estado = "ACTIVO", CreatedAt = ahora,
            });
            await ctx.SaveChangesAsync();
            return usuario.IdUsuario;
        }

        var (comercioA, aliadoA) = await ComercioAsync();
        var (comercioB, _)       = await ComercioAsync();
        return new Sembrado(
            comercioA, comercioB,
            await UsuarioAsync(aliadoA, comercioA, "ADMIN_COMERCIO"),
            await UsuarioAsync(aliadoA, comercioA, "ADMIN_SEDE_COMERCIO"),
            await UsuarioAsync(aliadoA, comercioA, "CAJERO"));
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-A comercio.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            await ctx.ComercioUsuarios.Where(u => creados.Aliados.Contains(u.IdComercioAliado)).ExecuteDeleteAsync();
            await ctx.RetirosComercio.Where(r => creados.Comercios.Contains(r.IdComercio)).ExecuteDeleteAsync();
            await ctx.ComerciosAliados.Where(a => creados.Aliados.Contains(a.IdComercioAliado)).ExecuteDeleteAsync();
            await ctx.Comercios.Where(c => creados.Comercios.Contains(c.IdComercio)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => creados.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => creados.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
