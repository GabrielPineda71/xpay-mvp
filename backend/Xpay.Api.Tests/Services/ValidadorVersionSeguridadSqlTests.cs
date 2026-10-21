using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — ValidadorVersionSeguridad contra SQL Server real (migración 049).
// Mismo patrón que el resto de pruebas SQL: se salta en local sin cadena de
// conexión y es obligatorio en CI. Datos sintéticos propios y limpieza
// best-effort en finally. Clave JWT aleatoria generada aquí.
[Collection("SqlIntegration")]
public sealed class ValidadorVersionSeguridadSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Mensaje       = ValidadorVersionSeguridad.MensajeFallo;
    private const string Issuer        = "xpay-test-issuer";
    private const string Audience      = "xpay-test-audience";
    private const string Clave         = "Prueba#2026vv";

    [Fact]
    public async Task UsuarioInexistente_Falla()
    {
        if (!TryConnString(out var cs)) return;
        await using var db = NuevoContexto(cs);
        var ctx = Contexto(db, Principal(long.MaxValue, 0));
        await ValidadorVersionSeguridad.ValidarAsync(ctx);
        Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
    }

    [Theory]
    [InlineData(EstadosUsuario.PendienteVerificacion)]
    [InlineData(EstadosUsuario.Bloqueado)]
    [InlineData(EstadosUsuario.Inactivo)]
    public async Task UsuarioNoActivo_Falla(string estado)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, _) = await SembrarUsuarioAsync(cs, creados, estado);
            await using var db = NuevoContexto(cs);
            var ctx = Contexto(db, Principal(idUsuario, 0));
            await ValidadorVersionSeguridad.ValidarAsync(ctx);
            Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Activo_VersionDistinta_Falla(int versionClaim)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, _) = await SembrarUsuarioAsync(cs, creados, EstadosUsuario.Activo);
            await IncrementarAsync(cs, idUsuario); // BD = 1
            await using var db = NuevoContexto(cs);
            var ctx = Contexto(db, Principal(idUsuario, versionClaim));
            await ValidadorVersionSeguridad.ValidarAsync(ctx);
            Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Activo_VersionCorrecta_Acepta_ConUnaSolaConsulta()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, _) = await SembrarUsuarioAsync(cs, creados, EstadosUsuario.Activo);
            await IncrementarAsync(cs, idUsuario); // BD = 1
            var contador = new ContadorComandos();
            await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>()
                .UseSqlServer(cs).AddInterceptors(contador).Options);
            var principal = Principal(idUsuario, 1);
            var ctx = Contexto(db, principal);

            await ValidadorVersionSeguridad.ValidarAsync(ctx);

            Assert.Null(ctx.Result);
            Assert.Same(principal, ctx.Principal);
            Assert.Equal(1, contador.Total);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // Extremo a extremo: token real emitido por LoginAsync, validado con los
    // mismos parámetros que Program.cs; tras incrementar la versión, el MISMO
    // token deja de ser vigente.
    [Fact]
    public async Task TokenDeLogin_EsVigente_YDejaDeSerloTrasIncrementar()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, nombre) = await SembrarUsuarioAsync(cs, creados, EstadosUsuario.Activo);
            var claveJwt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            string token;
            await using (var c = NuevoContexto(cs))
                token = (await new AuthService(c, Config(claveJwt))
                    .LoginAsync(new LoginRequest { Usuario = nombre, Password = Clave })).Token;

            await using (var db = NuevoContexto(cs))
            {
                var ctx = Contexto(db, await PrincipalDelPipelineAsync(token, claveJwt));
                await ValidadorVersionSeguridad.ValidarAsync(ctx);
                Assert.Null(ctx.Result);
            }

            await IncrementarAsync(cs, idUsuario);

            await using (var db = NuevoContexto(cs))
            {
                var ctx = Contexto(db, await PrincipalDelPipelineAsync(token, claveJwt));
                await ValidadorVersionSeguridad.ValidarAsync(ctx);
                Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
            }
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private static ClaimsPrincipal Principal(long idUsuario, int version)
        => new(new ClaimsIdentity(new[]
        {
            new Claim("idUsuario", idUsuario.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(VersionSeguridadUsuario.Claim, version.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        }, "Bearer"));

    private static TokenValidatedContext Contexto(XpayDbContext db, ClaimsPrincipal principal)
    {
        var servicios = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var http      = new DefaultHttpContext { RequestServices = servicios };
        var esquema   = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(http, esquema, new JwtBearerOptions()) { Principal = principal };
    }

    private static IConfiguration Config(string claveJwt) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = claveJwt, ["Jwt:Issuer"] = Issuer, ["Jwt:Audience"] = Audience,
        })
        .Build();

    // Mismos TokenValidationParameters que Program.cs (firma, emisor,
    // audiencia, vigencia) y mapeo de entrada por defecto del pipeline.
    private static async Task<ClaimsPrincipal> PrincipalDelPipelineAsync(string token, string claveJwt)
    {
        var resultado = await new JsonWebTokenHandler { MapInboundClaims = true }.ValidateTokenAsync(token,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(claveJwt)),
                ValidateIssuer = true, ValidIssuer = Issuer,
                ValidateAudience = true, ValidAudience = Audience,
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(60),
            });
        Assert.True(resultado.IsValid);
        return new ClaimsPrincipal(resultado.ClaimsIdentity);
    }

    private static async Task IncrementarAsync(string cs, long idUsuario)
    {
        await using var ctx = NuevoContexto(cs);
        await using var tx = await ctx.Database.BeginTransactionAsync();
        await VersionSeguridadUsuario.IncrementarAsync(ctx, idUsuario);
        await tx.CommitAsync();
    }

    private sealed class ContadorComandos : DbCommandInterceptor
    {
        private int _total;
        public int Total => _total;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Interlocked.Increment(ref _total); return base.ReaderExecuting(command, eventData, result); }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref _total); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref _total); return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref _total); return base.ScalarExecutingAsync(command, eventData, result, cancellationToken); }
    }

    private static async Task<(long IdUsuario, string Nombre)> SembrarUsuarioAsync(string cs, Sembrados creados, string estado)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora  = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"P0W{sufijo}",
            PrimerNombre = "P0W", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);

        var nombre  = $"p0w_vv_{sufijo}";
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = nombre,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave),
            Estado = estado, FechaCreacion = ahora,
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
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B validador vs.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            await ctx.Usuarios.Where(u => creados.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => creados.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
