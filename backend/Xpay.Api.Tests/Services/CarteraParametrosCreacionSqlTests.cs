using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Cada caso usa una tabla en un esquema propio de la base ficticia local/CI.
// No borra, reemplaza ni siembra los parámetros globales de la aplicación.
[Collection("SqlIntegration")]
public sealed class CarteraParametrosCreacionSqlTests
{
    private static UpsertParametroUtilizacionRequest Peticion(decimal tasa = 2m) =>
        new(tasa, 1m, 0m, false, 0m, 1, 12, "MENSUAL", 50000m, 500000m);

    [Fact]
    public void Modelo_de_pruebas_aisla_cada_esquema_sin_abrir_conexion()
    {
        var options = new DbContextOptionsBuilder<XpayDbContext>()
            .UseSqlServer("Server=localhost;Database=XPAY_MODELO_SIN_DATOS;Integrated Security=true;TrustServerCertificate=True;")
            .ReplaceService<IModelCacheKeyFactory, ClaveModeloEsquema>().Options;
        using var primero = new ContextoEsquema(options, "xpay_param_modelo_a");
        using var segundo = new ContextoEsquema(options, "xpay_param_modelo_b");
        Assert.NotSame(primero.Model, segundo.Model);
        foreach (var db in new[] { primero, segundo })
        {
            var parametros = db.Model.FindEntityType(typeof(CarteraParametroUtilizacion))!;
            Assert.Equal("cartera_parametros_utilizacion", parametros.GetTableName());
            Assert.Equal(db.Esquema, parametros.GetSchema());
            var usuarios = db.Model.FindEntityType(typeof(Usuario))!;
            Assert.Equal("usuarios", usuarios.GetTableName());
            Assert.Null(usuarios.GetSchema());
            Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        }
    }

    [Fact]
    public async Task Crear_responde_201_y_guarda_solo_la_modalidad_solicitada()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new EsquemaPropio(cs); await s.IniciarAsync();
        await using var db = s.Contexto();
        var respuesta = Assert.IsType<CreatedAtActionResult>(await Controller(db, 101).CrearParametro(" avance_wallet ", Peticion()));
        Assert.Equal(StatusCodes.Status201Created, respuesta.StatusCode);
        Assert.Equal(nameof(CarteraOrdinariaController.GetParametroPublico), respuesta.ActionName);
        var dto = Assert.IsType<ParametroUtilizacionDto>(respuesta.Value);
        Assert.Equal("AVANCE_WALLET", dto.TipoUtilizacion);
        Assert.Equal("ACTIVO", dto.Estado);
        Assert.Equal(Peticion().TasaEmv, dto.TasaEmv);
        Assert.Equal(Peticion().MontoMax, dto.MontoMax);
        var row = Assert.Single(await db.CarteraParametrosUtilizacion.AsNoTracking().ToListAsync());
        Assert.Equal(101L, row.CreatedByUsuario);
        Assert.Equal(101L, row.UpdatedByUsuario);
        Assert.True(row.CreatedAt > DateTime.MinValue);
        Assert.NotNull(row.UpdatedAt);
    }

    [Fact]
    public async Task Creacion_duplicada_responde_409_sin_cambiar_condiciones_o_actor()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new EsquemaPropio(cs); await s.IniciarAsync();
        await using var primero = s.Contexto();
        var original = await Servicio(primero).CrearParametroAsync("COMPRA_COMERCIO", Peticion(), 101);
        var fecha = await primero.CarteraParametrosUtilizacion.AsNoTracking().Select(p => p.UpdatedAt).SingleAsync();
        await using var segundo = s.Contexto();
        var intento = await Controller(segundo, 202).CrearParametro("COMPRA_COMERCIO",
            Peticion(3m) with { PorcAval = 2m, MontoMax = 900000m });
        Assert.IsType<ConflictObjectResult>(intento);
        Assert.Equal(original, Assert.Single(await Servicio(segundo).GetParametrosAsync()));
        var fila = await segundo.CarteraParametrosUtilizacion.AsNoTracking().SingleAsync();
        Assert.Equal(101L, fila.CreatedByUsuario);
        Assert.Equal(101L, fila.UpdatedByUsuario);
        Assert.Equal(fecha, fila.UpdatedAt);
        Assert.DoesNotContain(segundo.ChangeTracker.Entries<CarteraParametroUtilizacion>(),
            e => e.State == EntityState.Added);
    }

    [Fact]
    public async Task Modalidad_inactiva_tambien_conflicta_y_no_se_reactiva()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new EsquemaPropio(cs); await s.IniciarAsync();
        await using var db = s.Contexto();
        await Servicio(db).CrearParametroAsync("COMPRA_COMERCIO", Peticion(), 101);
        var fila = await db.CarteraParametrosUtilizacion.SingleAsync();
        fila.Estado = "INACTIVO"; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var original = Assert.Single(await Servicio(db).GetParametrosAsync());
        await Assert.ThrowsAsync<CarteraParametroDuplicadoException>(() =>
            Servicio(db).CrearParametroAsync("COMPRA_COMERCIO", Peticion(3m), 202));
        Assert.Equal(original, Assert.Single(await Servicio(db).GetParametrosAsync()));
        Assert.Equal("INACTIVO", original.Estado);
    }

    [Fact]
    public async Task Dos_INSERT_concurrentes_solo_crean_una_fila_sin_sobrescribir_al_ganador()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new EsquemaPropio(cs); await s.IniciarAsync();
        var barrera = new BarreraInsert(s.Esquema);
        async Task<(ParametroUtilizacionDto? Dto, Exception? Error, long Actor)> Intentar(decimal tasa, long actor)
        {
            await using var db = s.Contexto(barrera);
            try { return (await Servicio(db).CrearParametroAsync("AVANCE_WALLET", Peticion(tasa), actor), null, actor); }
            catch (Exception ex) { return (null, ex, actor); }
        }
        var resultados = await Task.WhenAll(Intentar(2m, 101), Intentar(3m, 202));
        Assert.Equal(2, barrera.Llegadas);
        var ganador = Assert.Single(resultados, r => r.Error is null);
        Assert.IsType<CarteraParametroDuplicadoException>(Assert.Single(resultados, r => r.Error is not null).Error);
        await using var lectura = s.Contexto();
        var persistida = Assert.Single(await Servicio(lectura).GetParametrosAsync());
        Assert.Equal(ganador.Dto, persistida);
        var fila = await lectura.CarteraParametrosUtilizacion.AsNoTracking().SingleAsync();
        Assert.Equal(ganador.Actor, fila.CreatedByUsuario);
        Assert.Equal(ganador.Actor, fila.UpdatedByUsuario);
    }

    [Fact]
    public async Task Colision_PK_ajena_no_se_disfraza_como_modalidad_duplicada()
    {
        if (!Conexion(out var cs)) return;
        // PK con DEFAULT1, solo en esta tabla ficticia: el segundo tipo
        // provoca 2627 por PK, aunque su tipo todavía no existe.
        await using var s = new EsquemaPropio(cs, pkFija: true); await s.IniciarAsync();
        await using var db = s.Contexto();
        var original = await Servicio(db).CrearParametroAsync("COMPRA_COMERCIO", Peticion(), 101);
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Servicio(db).CrearParametroAsync("AVANCE_WALLET", Peticion(3m), 202));
        Assert.Equal(original, Assert.Single(await Servicio(db).GetParametrosAsync()));
    }

    [Fact]
    public async Task PUT_valida_antes_de_modificar_y_conserva_la_edicion_existente()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new EsquemaPropio(cs); await s.IniciarAsync();
        await using var db = s.Contexto();
        var original = await Servicio(db).CrearParametroAsync("AVANCE_WALLET", Peticion(), 101);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Servicio(db).UpsertParametroAsync("AVANCE_WALLET", Peticion() with { PlazoMin = 0 }, 202));
        Assert.Equal(original, Assert.Single(await Servicio(db).GetParametrosAsync()));
        var edited = Assert.IsType<OkObjectResult>(await Controller(db, 202).UpsertParametro("avance_wallet",
            Peticion(3m) with { Frecuencia = " quincenal ", MontoMax = 900000m }));
        var dto = Assert.IsType<ParametroUtilizacionDto>(edited.Value);
        Assert.Equal(original.IdParametro, dto.IdParametro);
        Assert.Equal(3m, dto.TasaEmv);
        Assert.Equal("QUINCENAL", dto.Frecuencia);
        Assert.Equal(900000m, dto.MontoMax);
        var fila = await db.CarteraParametrosUtilizacion.AsNoTracking().SingleAsync();
        Assert.Equal(101L, fila.CreatedByUsuario);
        Assert.Equal(202L, fila.UpdatedByUsuario);
    }

    private static CarteraOrdinariaService Servicio(XpayDbContext db) =>
        new(db, null!, NullLogger<CarteraOrdinariaService>.Instance);

    private static CarteraOrdinariaController Controller(XpayDbContext db, long actor) =>
        new(Servicio(db), db, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("idUsuario", actor.ToString())], "test")),
            } },
        };

    private static bool Conexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? "";
        if (string.IsNullOrWhiteSpace(cs))
        {
            Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
                "ConnectionStrings__XpayConnection es obligatoria en CI para probar creación de parámetros.");
            return false;
        }
        var b = new SqlConnectionStringBuilder(cs);
        var host = b.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase).Split(',')[0];
        Assert.True((host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "::1" || host == "[::1]")
            && b.InitialCatalog.StartsWith("XPAY_", StringComparison.OrdinalIgnoreCase),
            "Estas pruebas solo permiten una base ficticia XPAY_ en SQL loopback.");
        return true;
    }

    private sealed class EsquemaPropio(string cs, bool pkFija = false) : IAsyncDisposable
    {
        public string Esquema { get; } = "xpay_param_test_" + Guid.NewGuid().ToString("N");

        public async Task IniciarAsync()
        {
            var id = pkFija ? "BIGINT NOT NULL DEFAULT 1 PRIMARY KEY" : "BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY";
            await EjecutarAsync($"CREATE SCHEMA [{Esquema}];");
            await EjecutarAsync($"""
                CREATE TABLE [{Esquema}].[cartera_parametros_utilizacion](
                  id_parametro {id}, tipo_utilizacion VARCHAR(30) NOT NULL,
                  tasa_emv DECIMAL(9,4) NOT NULL, porc_aval DECIMAL(9,4) NOT NULL, porc_admin DECIMAL(9,4) NOT NULL,
                  aplica_iva BIT NOT NULL, porc_iva DECIMAL(9,4) NOT NULL,
                  plazo_min INT NOT NULL, plazo_max INT NOT NULL, frecuencia VARCHAR(20) NOT NULL,
                  monto_min DECIMAL(18,2) NOT NULL, monto_max DECIMAL(18,2) NOT NULL, estado VARCHAR(20) NOT NULL,
                  created_at DATETIME2 NOT NULL, updated_at DATETIME2 NULL,
                  created_by_usuario BIGINT NULL, updated_by_usuario BIGINT NULL,
                  CONSTRAINT uq_cart_param_util_tipo UNIQUE(tipo_utilizacion));
                """);
        }

        public XpayDbContext Contexto(params IInterceptor[] interceptores)
        {
            var builder = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs)
                .ReplaceService<IModelCacheKeyFactory, ClaveModeloEsquema>();
            if (interceptores.Length > 0) builder.AddInterceptors(interceptores);
            return new ContextoEsquema(builder.Options, Esquema);
        }

        private async Task EjecutarAsync(string sql)
        {
            await using var c = new SqlConnection(cs);
            await c.OpenAsync();
            await using var q = c.CreateCommand(); q.CommandText = sql; q.CommandTimeout = 30;
            await q.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync() => await EjecutarAsync(
            $"DROP TABLE IF EXISTS [{Esquema}].[cartera_parametros_utilizacion]; IF SCHEMA_ID(N'{Esquema}') IS NOT NULL DROP SCHEMA [{Esquema}];");
    }

    private sealed class ContextoEsquema(DbContextOptions<XpayDbContext> options, string esquema) : XpayDbContext(options)
    {
        public string Esquema { get; } = esquema;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CarteraParametroUtilizacion>().ToTable("cartera_parametros_utilizacion", Esquema);
        }
    }

    public sealed class ClaveModeloEsquema : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is ContextoEsquema c
            ? (context.GetType(), c.Esquema, designTime)
            : (object)(context.GetType(), designTime);
        public object Create(DbContext context) => Create(context, false);
    }

    private sealed class BarreraInsert(string esquema) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> ambas = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int llegadas;
        public int Llegadas => Volatile.Read(ref llegadas);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"[{esquema}].[cartera_parametros_utilizacion]", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref llegadas) == 2) ambas.TrySetResult(true);
                await ambas.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
