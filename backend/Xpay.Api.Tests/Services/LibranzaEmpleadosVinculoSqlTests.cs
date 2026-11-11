using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// D2 — la lista ADMIN de empleados indica el usuario con vínculo ACTIVO
// (GetEmpleadosConVinculoAsync); un vínculo INACTIVO o inexistente no cuenta.
// La lista de la vista de EMPRESA (GetEmpleadosAsync) nunca expone el
// identificador interno del usuario: ni el valor ni la clave en el JSON.
[Collection("SqlIntegration")]
public sealed class LibranzaEmpleadosVinculoSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    [Fact]
    public async Task ListaAdmin_IndicaSoloVinculoActivo_YListaEmpresa_NoExponeUsuario()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var s = await SembrarAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            var svc = new LibranzaEmpleadosService(ctx, NullLogger<LibranzaEmpleadosService>.Instance);

            var admin = (await svc.GetEmpleadosConVinculoAsync(s.IdConvenio)).ToDictionary(e => e.IdEmpleado);
            Assert.Equal(3, admin.Count);
            Assert.Equal(s.IdUsuarioActivo, admin[s.EmpleadoActivo].IdUsuarioVinculado);
            Assert.Null(admin[s.EmpleadoInactivo].IdUsuarioVinculado);
            Assert.Null(admin[s.EmpleadoSinVinculo].IdUsuarioVinculado);

            var empresa = await svc.GetEmpleadosAsync(s.IdConvenio);
            Assert.Equal(3, empresa.Count);
            Assert.All(empresa, e => Assert.Null(e.IdUsuarioVinculado));

            // Mismo serializador que ASP.NET Core (JsonSerializerDefaults.Web).
            var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var jsonEmpresa = JsonSerializer.Serialize(empresa, web);
            Assert.DoesNotContain("idUsuarioVinculado", jsonEmpresa, StringComparison.OrdinalIgnoreCase);
            var jsonAdmin = JsonSerializer.Serialize(admin.Values, web);
            Assert.Contains($"\"idUsuarioVinculado\":{s.IdUsuarioActivo}", jsonAdmin);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    private sealed record Sembrado(
        long IdConvenio, long EmpleadoActivo, long EmpleadoInactivo, long EmpleadoSinVinculo, long IdUsuarioActivo);

    private sealed class Creados
    {
        public List<long> Personas  { get; } = new();
        public List<long> Usuarios  { get; } = new();
        public List<long> Empleados { get; } = new();
        public long IdConvenio;
    }

    private static async Task<Sembrado> SembrarAsync(string cs, Creados c)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var ahora  = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];

        async Task<long> UsuarioAsync(string n)
        {
            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad, Celular = "3000000000", Email = $"d2.{n}.{sufijo}@example.test",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            c.Personas.Add(persona.IdPersona);
            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"d2_{n}_{sufijo}",
                PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            c.Usuarios.Add(usuario.IdUsuario);
            return usuario.IdUsuario;
        }

        var uActivo   = await UsuarioAsync("act");
        var uInactivo = await UsuarioAsync("ina");

        var convenio = new LibranzaEmpresaConvenio
        {
            NombreEmpresa = $"Conv d2 {sufijo}",
            Nit = $"9{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}",
            Estado = "ACTIVO", PeriodicidadPago = "MENSUAL", PorcentajeMaximoCupo = 30m,
            FechaInicio = ahora, CreatedAt = ahora,
        };
        ctx.LibranzaEmpresasConvenio.Add(convenio);
        await ctx.SaveChangesAsync();
        c.IdConvenio = convenio.IdConvenio;

        async Task<long> EmpleadoAsync(string n)
        {
            var e = new LibranzaEmpleado
            {
                IdConvenio = convenio.IdConvenio, TipoDocumento = "CC",
                NumeroDocumento = $"66{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                Nombres = $"D2{n}", SalarioMensual = 1_000_000m, PeriodicidadPago = "MENSUAL", Estado = "ACTIVO",
                CupoPreliminar = 300_000m, OrigenCarga = "MANUAL", CreatedAt = ahora,
            };
            ctx.LibranzaEmpleados.Add(e);
            await ctx.SaveChangesAsync();
            c.Empleados.Add(e.IdEmpleado);
            return e.IdEmpleado;
        }

        var eActivo   = await EmpleadoAsync("Act");
        var eInactivo = await EmpleadoAsync("Ina");
        var eSin      = await EmpleadoAsync("Sin");

        ctx.LibranzaEmpleadoUsuarios.Add(new LibranzaEmpleadoUsuario
            { IdEmpleado = eActivo, IdUsuario = uActivo, Estado = "ACTIVO", CreatedAt = ahora });
        ctx.LibranzaEmpleadoUsuarios.Add(new LibranzaEmpleadoUsuario
            { IdEmpleado = eInactivo, IdUsuario = uInactivo, Estado = "INACTIVO", CreatedAt = ahora });
        await ctx.SaveChangesAsync();

        return new Sembrado(convenio.IdConvenio, eActivo, eInactivo, eSin, uActivo);
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de D2.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Mismo orden de borrado que LibranzaAnticipoMultipleTests (FK primero). Best-effort.
    private static async Task LimpiarAsync(string cs, Creados c)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in c.Empleados)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empleado_usuario WHERE id_empleado = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empleados WHERE id_empleado = {id}");
            }
            await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empresas_convenio WHERE id_convenio = {c.IdConvenio}");
            foreach (var id in c.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in c.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
