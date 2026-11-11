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

// D10 — LoginResponse.EsUsuarioEmpresa refleja una asociación ACTIVA en
// libranza_usuarios_empresa (antes la vista empresa dependía del nombre fijo
// qa.empresa1). Una asociación INACTIVA o inexistente no habilita la vista.
[Collection("SqlIntegration")]
public sealed class AuthLoginEsUsuarioEmpresaSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Clave         = "Login#2026emp";

    [Theory]
    [InlineData(null, false)]
    [InlineData("ACTIVO", true)]
    [InlineData("INACTIVO", false)]
    public async Task Login_EsUsuarioEmpresa_SegunAsociacion(string? estadoAsociacion, bool esperado)
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var (idUsuario, nombre) = await SembrarAsync(cs, c, estadoAsociacion);
            await using var ctx = NuevoContexto(cs);
            var r = await Servicio(ctx).LoginAsync(new LoginRequest { Usuario = nombre, Password = Clave });
            Assert.Equal(idUsuario, r.IdUsuario);
            Assert.Equal(esperado, r.EsUsuarioEmpresa);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    private sealed class Creados
    {
        public long IdPersona, IdUsuario, IdConvenio;
    }

    private static async Task<(long IdUsuario, string Nombre)> SembrarAsync(string cs, Creados c, string? estadoAsociacion)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var ahora = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, Celular = "3000000000", Email = $"d10.{sufijo}@example.test",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        c.IdPersona = persona.IdPersona;

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"d10_emp_{sufijo}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave),
            Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        c.IdUsuario = usuario.IdUsuario;

        if (estadoAsociacion is not null)
        {
            var convenio = new LibranzaEmpresaConvenio
            {
                NombreEmpresa = $"Conv d10 {sufijo}",
                Nit = $"9{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}",
                Estado = "ACTIVO", PeriodicidadPago = "MENSUAL", PorcentajeMaximoCupo = 30m,
                FechaInicio = ahora, CreatedAt = ahora,
            };
            ctx.LibranzaEmpresasConvenio.Add(convenio);
            await ctx.SaveChangesAsync();
            c.IdConvenio = convenio.IdConvenio;

            ctx.LibranzaUsuariosEmpresa.Add(new LibranzaUsuarioEmpresa
            {
                IdUsuario = usuario.IdUsuario, IdConvenio = convenio.IdConvenio,
                RolEmpresa = "ADMIN_EMPRESA", Estado = estadoAsociacion, CreatedAt = ahora,
            });
            await ctx.SaveChangesAsync();
        }
        return (usuario.IdUsuario, usuario.NombreUsuario);
    }

    private static AuthService Servicio(XpayDbContext ctx) => new(ctx, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"]      = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"]   = "xpay-test-issuer",
            ["Jwt:Audience"] = "xpay-test-audience",
        })
        .Build());

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de D10.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // FK primero. Best-effort, como en el resto de pruebas SQL.
    private static async Task LimpiarAsync(string cs, Creados c)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            await ctx.LibranzaUsuariosEmpresa.Where(a => a.IdUsuario == c.IdUsuario).ExecuteDeleteAsync();
            await ctx.LibranzaEmpresasConvenio.Where(x => x.IdConvenio == c.IdConvenio).ExecuteDeleteAsync();
            await ctx.Auditorias.Where(x => x.IdUsuario == c.IdUsuario).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => u.IdUsuario == c.IdUsuario).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => p.IdPersona == c.IdPersona).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort */ }
    }
}
