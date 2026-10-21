using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — emisión del claim "vs" (usuarios.version_seguridad). GenerarJwt sigue
// siendo privado: el claim se valida a través de los métodos públicos que
// emiten JWT (LoginAsync y CambiarClaveObligatoriaAsync) contra SQL Server
// local con datos sintéticos propios. Solo el caso de versión negativa, que la
// BD no puede producir (CHECK >= 0 de la migración 049), invoca el método
// privado por reflexión sin ampliar su visibilidad. Clave JWT aleatoria.
[Collection("SqlIntegration")]
public sealed class AuthServiceClaimVsTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Issuer        = "xpay-test-issuer";
    private const string Audience      = "xpay-test-audience";
    private const string Clave         = "Prueba#2026vs";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Login_EmiteExactamenteUnVs_ConLaVersionDelUsuario(int incrementos)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, nombre, _) = await SembrarUsuarioAsync(cs, creados);
            await IncrementarAsync(cs, idUsuario, incrementos);

            var jwt = await LoginAsync(cs, nombre);

            Assert.Equal(incrementos.ToString(CultureInfo.InvariantCulture), Valor(jwt, VersionSeguridadUsuario.Claim));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Login_VsNoDependeDeLaCulturaActual()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        var original = CultureInfo.CurrentCulture;
        try
        {
            var (idUsuario, nombre, _) = await SembrarUsuarioAsync(cs, creados);
            await IncrementarAsync(cs, idUsuario, 2);

            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var jwt = await LoginAsync(cs, nombre);

            Assert.Equal("2", Valor(jwt, VersionSeguridadUsuario.Claim));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            await LimpiarAsync(cs, creados);
        }
    }

    [Fact]
    public async Task Login_ClaimsExistentesNoCambian()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, nombre, idPersona) = await SembrarUsuarioAsync(cs, creados, conRolUsuarioFinal: true);

            var jwt = await LoginAsync(cs, nombre);

            Assert.Equal(Issuer, jwt.Issuer);
            Assert.Equal(Audience, Assert.Single(jwt.Audiences));
            Assert.Equal(idUsuario.ToString(CultureInfo.InvariantCulture), Valor(jwt, "idUsuario"));
            Assert.Equal(idPersona.ToString(CultureInfo.InvariantCulture), Valor(jwt, "idPersona"));
            Assert.Equal(nombre, Valor(jwt, "usuario"));
            Assert.Equal(idUsuario.ToString(CultureInfo.InvariantCulture), Valor(jwt, JwtRegisteredClaimNames.Sub));
            Assert.True(Guid.TryParse(Valor(jwt, JwtRegisteredClaimNames.Jti), out _));
            Assert.Equal("false", Valor(jwt, "requiereCambioClave"));
            Assert.Equal("USUARIO_FINAL", Valor(jwt, ClaimTypes.Role));
            Assert.Equal("0", Valor(jwt, VersionSeguridadUsuario.Claim));
            Assert.True(jwt.ValidTo > DateTime.UtcNow);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task CambiarClaveObligatoria_EmiteElVsActual()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, _, _) = await SembrarUsuarioAsync(cs, creados, requiereCambioClave: true);
            await IncrementarAsync(cs, idUsuario, 2);

            await using var ctx = NuevoContexto(cs);
            var respuesta = await new AuthService(ctx, Config()).CambiarClaveObligatoriaAsync(idUsuario,
                new CambiarClaveObligatoriaRequest
                {
                    ClaveActual = Clave, ClaveNueva = "Nueva#2026xyz", ConfirmacionClaveNueva = "Nueva#2026xyz",
                });
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(respuesta.Token);

            // 2 en BD + revocación del propio cambio obligatorio: el token nuevo lleva 3.
            Assert.Equal("3", Valor(jwt, VersionSeguridadUsuario.Claim));
            Assert.Equal("false", Valor(jwt, "requiereCambioClave"));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // Sin BD. Ninguna fila puede tener versión negativa (CHECK de la 049), así
    // que el fallo cerrado de GenerarJwt solo se alcanza invocándolo directamente.
    [Fact]
    public void GenerarJwt_SiguePrivado_YVersionNegativaFalla()
    {
        var metodo = typeof(AuthService).GetMethod("GenerarJwt", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(metodo);
        Assert.True(metodo!.IsPrivate);

        var ex = Assert.Throws<TargetInvocationException>(() => metodo.Invoke(
            new AuthService(null!, Config()),
            new object[] { 7L, 8L, "usuario.prueba", new List<string>(), false, -1 }));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"]      = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"]   = Issuer,
            ["Jwt:Audience"] = Audience,
        })
        .Build();

    private static string Valor(JwtSecurityToken jwt, string tipo)
        => Assert.Single(jwt.Claims, c => c.Type == tipo).Value;

    private static async Task<JwtSecurityToken> LoginAsync(string cs, string nombre)
    {
        await using var ctx = NuevoContexto(cs);
        var respuesta = await new AuthService(ctx, Config())
            .LoginAsync(new LoginRequest { Usuario = nombre, Password = Clave });
        return new JwtSecurityTokenHandler().ReadJwtToken(respuesta.Token);
    }

    private static async Task IncrementarAsync(string cs, long idUsuario, int veces)
    {
        for (var i = 0; i < veces; i++)
        {
            await using var ctx = NuevoContexto(cs);
            await using var tx = await ctx.Database.BeginTransactionAsync();
            await VersionSeguridadUsuario.IncrementarAsync(ctx, idUsuario);
            await tx.CommitAsync();
        }
    }

    private static async Task<(long IdUsuario, string Nombre, long IdPersona)> SembrarUsuarioAsync(
        string cs, Sembrados creados, bool requiereCambioClave = false, bool conRolUsuarioFinal = false)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora  = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"P0J{sufijo}",
            PrimerNombre = "P0J", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);

        var nombre  = $"p0j_vs_{sufijo}";
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = nombre,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave),
            RequiereCambioClave = requiereCambioClave,
            Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);

        if (conRolUsuarioFinal)
        {
            var idRol = await ctx.Roles.Where(r => r.Codigo == "USUARIO_FINAL").Select(r => r.IdRol).SingleAsync();
            ctx.UsuarioRoles.Add(new UsuarioRol
            {
                IdUsuario = usuario.IdUsuario, IdRol = idRol, Estado = "ACTIVO", FechaAsignacion = ahora,
            });
            await ctx.SaveChangesAsync();
        }

        return (usuario.IdUsuario, nombre, persona.IdPersona);
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
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B claim vs.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            await ctx.UsuarioRoles.Where(r => creados.Usuarios.Contains(r.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => creados.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => creados.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
