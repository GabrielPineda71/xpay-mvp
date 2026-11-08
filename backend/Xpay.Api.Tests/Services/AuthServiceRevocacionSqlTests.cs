using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — revocación atómica de version_seguridad en AuthService (cambio de
// clave voluntario/obligatorio y bloqueo por 5.º fallo de login) contra SQL
// Server local. Datos sintéticos propios y limpieza best-effort en finally
// (auditoria antes que usuario por FK). Clave JWT aleatoria generada aquí.
[Collection("SqlIntegration")]
public sealed class AuthServiceRevocacionSqlTests
{
    private const string EnvConnString  = "ConnectionStrings__XpayConnection";
    private const string Clave          = "Prueba#2026rv";
    private const string ClaveNueva     = "Nueva#2026rvx";
    private const string ClaveErronea   = "Erronea#2026rv";
    private const string MotivoBloqueo  = "Bloqueo automático por intentos fallidos.";
    private const string MsjCredencial  = "Usuario o contraseña inválidos.";

    // ── Cambio voluntario ───────────────────────────────────────────────

    [Fact]
    public async Task Voluntario_Correcto_RevocaYAudita()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, _) = await SembrarUsuarioAsync(cs, creados);
            await using (var ctx = NuevoContexto(cs))
                await Servicio(ctx).CambiarClaveVoluntariaAsync(id, new CambiarClaveRequest { ClaveActual = Clave, ClaveNueva = ClaveNueva });

            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(1, u.VersionSeguridad);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveNueva, u.PasswordHash));
            Assert.Equal(1, await AuditoriasAsync(cs, id));
            Assert.False(await VigenteAsync(cs, id, 0));
            Assert.True(await VigenteAsync(cs, id, 1));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Voluntario_ClaveActualIncorrecta_NoRevoca()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, _) = await SembrarUsuarioAsync(cs, creados);
            await using (var ctx = NuevoContexto(cs))
                await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(ctx).CambiarClaveVoluntariaAsync(
                    id, new CambiarClaveRequest { ClaveActual = ClaveErronea, ClaveNueva = ClaveNueva }));

            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(0, u.VersionSeguridad);
            Assert.True(BCrypt.Net.BCrypt.Verify(Clave, u.PasswordHash));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Voluntario_FallaElIncremento_RevierteTodo()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, _) = await SembrarUsuarioAsync(cs, creados);
            await using (var ctx = ContextoQueFallaAlIncrementar(cs))
                await AssertFalloInyectadoAsync(() => Servicio(ctx).CambiarClaveVoluntariaAsync(
                    id, new CambiarClaveRequest { ClaveActual = Clave, ClaveNueva = ClaveNueva }));

            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(0, u.VersionSeguridad);
            Assert.True(BCrypt.Net.BCrypt.Verify(Clave, u.PasswordHash));
            Assert.Equal(0, await AuditoriasAsync(cs, id));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Cambio obligatorio ──────────────────────────────────────────────

    [Fact]
    public async Task Obligatorio_Correcto_RevocaYEmiteTokenConVersionFresca()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, _) = await SembrarUsuarioAsync(cs, creados, requiereCambioClave: true);
            await IncrementarAsync(cs, id, 2); // BD = 2

            LoginResponse respuesta;
            await using (var ctx = NuevoContexto(cs))
                respuesta = await Servicio(ctx).CambiarClaveObligatoriaAsync(id, Obligatorio(Clave, ClaveNueva, ClaveNueva));

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(respuesta.Token);
            Assert.Equal("3", Assert.Single(jwt.Claims, c => c.Type == VersionSeguridadUsuario.Claim).Value);
            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(3, u.VersionSeguridad);
            Assert.False(u.RequiereCambioClave);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveNueva, u.PasswordHash));
            Assert.True(await VigenteAsync(cs, id, 3));
            Assert.False(await VigenteAsync(cs, id, 2));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Obligatorio_ConfirmacionDistinta_NoRevoca()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, _) = await SembrarUsuarioAsync(cs, creados, requiereCambioClave: true);
            await using (var ctx = NuevoContexto(cs))
                await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(ctx)
                    .CambiarClaveObligatoriaAsync(id, Obligatorio(Clave, ClaveNueva, "Otra#2026rvx")));

            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(0, u.VersionSeguridad);
            Assert.True(u.RequiereCambioClave);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Obligatorio_FallaElIncremento_RevierteTodo()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, _) = await SembrarUsuarioAsync(cs, creados, requiereCambioClave: true);
            await using (var ctx = ContextoQueFallaAlIncrementar(cs))
                await AssertFalloInyectadoAsync(() => Servicio(ctx)
                    .CambiarClaveObligatoriaAsync(id, Obligatorio(Clave, ClaveNueva, ClaveNueva)));

            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(0, u.VersionSeguridad);
            Assert.True(u.RequiereCambioClave);
            Assert.True(BCrypt.Net.BCrypt.Verify(Clave, u.PasswordHash));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Login: bloqueo por intentos fallidos ────────────────────────────

    [Fact]
    public async Task Login_Fallos1a4_NoIncrementan()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, nombre) = await SembrarUsuarioAsync(cs, creados);
            for (var intento = 1; intento <= 4; intento++)
            {
                Assert.Equal(MsjCredencial, await LoginFallidoAsync(cs, nombre));
                var u = await LeerUsuarioAsync(cs, id);
                Assert.Equal(0, u.VersionSeguridad);
                Assert.Equal(EstadosUsuario.Activo, u.Estado);
                Assert.Equal(intento, u.IntentosFallidos);
            }
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Login_QuintoFallo_BloqueaYRevoca_YElSextoNoIncrementa()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, nombre) = await SembrarUsuarioAsync(cs, creados);
            for (var intento = 1; intento <= 5; intento++)
                Assert.Equal(MsjCredencial, await LoginFallidoAsync(cs, nombre));

            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(1, u.VersionSeguridad);
            Assert.Equal(EstadosUsuario.Bloqueado, u.Estado);
            Assert.NotNull(u.FechaBloqueo);
            Assert.Equal(MotivoBloqueo, u.MotivoBloqueo);
            Assert.False(await VigenteAsync(cs, id, 0));

            // 6.º intento sobre BLOQUEADO: no cuenta ni incrementa; P0-B3 — misma
            // respuesta pública que cualquier credencial inválida.
            Assert.Equal(MsjCredencial, await LoginFallidoAsync(cs, nombre));
            Assert.Equal(1, (await LeerUsuarioAsync(cs, id)).VersionSeguridad);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Login_CorrectoTrasCuatroFallos_NoIncrementa_YReiniciaContador()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, nombre) = await SembrarUsuarioAsync(cs, creados);
            for (var intento = 1; intento <= 4; intento++)
                await LoginFallidoAsync(cs, nombre);

            await using (var ctx = NuevoContexto(cs))
                await Servicio(ctx).LoginAsync(new LoginRequest { Usuario = nombre, Password = Clave });

            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(0, u.VersionSeguridad);
            Assert.Equal(0, u.IntentosFallidos);
            Assert.Equal(EstadosUsuario.Activo, u.Estado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Login_QuintoFallo_FallaElIncremento_RevierteTodo()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (id, nombre) = await SembrarUsuarioAsync(cs, creados, intentosFallidos: 4);
            await using (var ctx = ContextoQueFallaAlIncrementar(cs))
                await AssertFalloInyectadoAsync(() => Servicio(ctx)
                    .LoginAsync(new LoginRequest { Usuario = nombre, Password = ClaveErronea }));

            var u = await LeerUsuarioAsync(cs, id);
            Assert.Equal(0, u.VersionSeguridad);
            Assert.Equal(EstadosUsuario.Activo, u.Estado);
            Assert.Equal(4, u.IntentosFallidos);
            Assert.Null(u.FechaBloqueo);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private static AuthService Servicio(XpayDbContext ctx) => new(ctx, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"]      = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"]   = "xpay-test-issuer",
            ["Jwt:Audience"] = "xpay-test-audience",
        })
        .Build());

    private static CambiarClaveObligatoriaRequest Obligatorio(string actual, string nueva, string confirmacion)
        => new() { ClaveActual = actual, ClaveNueva = nueva, ConfirmacionClaveNueva = confirmacion };

    private static async Task<string> LoginFallidoAsync(string cs, string nombre)
    {
        await using var ctx = NuevoContexto(cs);
        var ex = await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => Servicio(ctx).LoginAsync(new LoginRequest { Usuario = nombre, Password = ClaveErronea }));
        return ex.Message;
    }

    private static async Task<Usuario> LeerUsuarioAsync(string cs, long id)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == id);
    }

    private static async Task<int> AuditoriasAsync(string cs, long id)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Auditorias.AsNoTracking().CountAsync(a => a.IdUsuario == id && a.Accion == "CAMBIAR_CLAVE");
    }

    private static async Task<bool> VigenteAsync(string cs, long id, int version)
    {
        await using var ctx = NuevoContexto(cs);
        return await ValidadorVersionSeguridad.EsVigenteAsync(ctx, id, version, CancellationToken.None);
    }

    private static async Task IncrementarAsync(string cs, long id, int veces)
    {
        for (var i = 0; i < veces; i++)
        {
            await using var ctx = NuevoContexto(cs);
            await using var tx = await ctx.Database.BeginTransactionAsync();
            await VersionSeguridadUsuario.IncrementarAsync(ctx, id);
            await tx.CommitAsync();
        }
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

    private static async Task<(long IdUsuario, string Nombre)> SembrarUsuarioAsync(
        string cs, Sembrados creados, bool requiereCambioClave = false, int intentosFallidos = 0)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora  = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"P0R{sufijo}",
            PrimerNombre = "P0R", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);

        var nombre  = $"p0r_rv_{sufijo}";
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = nombre,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave),
            RequiereCambioClave = requiereCambioClave, IntentosFallidos = intentosFallidos,
            Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);
        return (usuario.IdUsuario, nombre);
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
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B revocación.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            await ctx.Auditorias.Where(a => a.IdUsuario != null && creados.Usuarios.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => creados.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => creados.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
