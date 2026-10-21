using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — VersionSeguridadUsuario.IncrementarAsync contra SQL Server real
// (migración 049). Mismo patrón que TokenUsuarioServiceSqlTests: se salta en
// local sin cadena de conexión y es obligatorio en CI. Datos sintéticos
// propios y limpieza best-effort en finally.
[Collection("SqlIntegration")]
public sealed class VersionSeguridadUsuarioSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(30);

    // Sin BD: la validación de argumentos ocurre antes de cualquier acceso.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task IdUsuarioNoPositivo_Rechaza_SinTocarBd(long idUsuario)
    {
        await using var ctx = NuevoContexto("Server=127.0.0.1,1;Database=NoExiste;Connect Timeout=1;");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => VersionSeguridadUsuario.IncrementarAsync(ctx, idUsuario));
    }

    [Fact]
    public void Claim_EsVs()
        => Assert.Equal("vs", VersionSeguridadUsuario.Claim);

    [Fact]
    public async Task SinTransaccion_Falla_YNoIncrementa()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var idUsuario = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);

            await Assert.ThrowsAsync<VersionSeguridadException>(
                () => VersionSeguridadUsuario.IncrementarAsync(ctx, idUsuario));
            Assert.Equal(0, await VersionAsync(cs, idUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task UsuarioInexistente_EnTransaccion_Falla()
    {
        if (!TryConnString(out var cs)) return;
        await using var ctx = NuevoContexto(cs);
        await using var tx = await ctx.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<VersionSeguridadException>(
            () => VersionSeguridadUsuario.IncrementarAsync(ctx, long.MaxValue));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task IncrementoValido_SumaUno_YNoTocaOtrasColumnas()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var idUsuario = await SembrarUsuarioAsync(cs, creados);
            Usuario antes;
            await using (var r = NuevoContexto(cs))
                antes = await r.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == idUsuario);
            Assert.Equal(0, antes.VersionSeguridad); // mapeo: lee la columna con su DEFAULT

            await using (var ctx = NuevoContexto(cs))
            {
                await using var tx = await ctx.Database.BeginTransactionAsync();
                await VersionSeguridadUsuario.IncrementarAsync(ctx, idUsuario);
                await tx.CommitAsync();
            }

            await using var v = NuevoContexto(cs);
            var despues = await v.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == idUsuario);
            Assert.Equal(1, despues.VersionSeguridad);
            Assert.Equal(antes.Estado, despues.Estado);
            Assert.Equal(antes.PasswordHash, despues.PasswordHash);
            Assert.Equal(antes.IntentosFallidos, despues.IntentosFallidos);
            Assert.Equal(antes.RequiereCambioClave, despues.RequiereCambioClave);
            Assert.Equal(antes.FechaActualizacion, despues.FechaActualizacion);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Rollback_ElIncrementoNoPersiste()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var idUsuario = await SembrarUsuarioAsync(cs, creados);
            await using (var ctx = NuevoContexto(cs))
            {
                await using var tx = await ctx.Database.BeginTransactionAsync();
                await VersionSeguridadUsuario.IncrementarAsync(ctx, idUsuario);
                await tx.RollbackAsync();
            }
            Assert.Equal(0, await VersionAsync(cs, idUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // Ocho transacciones independientes incrementan a la vez el mismo usuario:
    // el UPDATE atómico no pierde ninguno (un read-modify-write sí podría).
    [Fact]
    public async Task Concurrencia_OchoTransacciones_NingunIncrementoPerdido()
    {
        const int n = 8;
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var idUsuario = await SembrarUsuarioAsync(cs, creados);
            var salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tareas = Enumerable.Range(0, n).Select(_ => Task.Run(async () =>
            {
                await using var ctx = NuevoContexto(cs);
                await salida.Task;
                await using var tx = await ctx.Database.BeginTransactionAsync();
                await VersionSeguridadUsuario.IncrementarAsync(ctx, idUsuario);
                await tx.CommitAsync();
            })).ToList();
            salida.SetResult();
            await Task.WhenAll(tareas).WaitAsync(EsperaMaxima);

            Assert.Equal(n, await VersionAsync(cs, idUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private static async Task<int> VersionAsync(string cs, long idUsuario)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => u.VersionSeguridad)
            .SingleAsync();
    }

    private static async Task<long> SembrarUsuarioAsync(string cs, Sembrados creados)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora  = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"P0V{sufijo}",
            PrimerNombre = "P0V", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"p0v_vs_{sufijo}",
            PasswordHash = "x", Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);
        return usuario.IdUsuario;
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
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B version_seguridad.");
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
