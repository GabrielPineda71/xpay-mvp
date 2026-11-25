using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Solo cuatro tablas ficticias en un esquema aleatorio propio. Ningún dato
// global, usuario, ledger ni operación financiera forma parte de esta prueba.
[Collection("SqlIntegration")]
public sealed class ReportesNombreContraparteSqlTests
{
    [Fact]
    public void Modelo_de_prueba_aisla_las_cuatro_tablas_sin_abrir_conexion()
    {
        var options = new DbContextOptionsBuilder<XpayDbContext>()
            .UseSqlServer("Server=localhost;Database=XPAY_MODELO_SIN_DATOS;Integrated Security=true;TrustServerCertificate=True;")
            .ReplaceService<IModelCacheKeyFactory, ClaveModeloEsquema>().Options;
        using var primero = new ContextoEsquema(options, "xpay_mov_modelo_a");
        using var segundo = new ContextoEsquema(options, "xpay_mov_modelo_b");
        Assert.NotSame(primero.Model, segundo.Model);
        foreach (var db in new[] { primero, segundo })
        {
            foreach (var tipo in new[] { typeof(Wallet), typeof(WalletSaldo), typeof(WalletMovimiento), typeof(Persona) })
                Assert.Equal(db.Esquema, db.Model.FindEntityType(tipo)!.GetSchema());
            Assert.Null(db.Model.FindEntityType(typeof(Usuario))!.GetSchema());
            Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        }
    }

    [Fact]
    public async Task Estado_cuenta_solo_enriquece_transferencias_propias_personales_y_sigue_siendo_lectura()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new EsquemaPropio(cs);
        await s.IniciarAsync();
        await s.SembrarAsync();
        var lecturas = new LecturasPropias(s.Esquema);
        await using var db = s.Contexto(lecturas);

        var resultado = JsonSerializer.SerializeToElement(await new ReportesService(db).GetEstadoCuentaWalletAsync(1));

        Assert.Equal(1, resultado.GetProperty("idWallet").GetInt64());
        Assert.Equal(1234m, resultado.GetProperty("saldoDisponible").GetDecimal());
        Assert.Equal(50m, resultado.GetProperty("saldoRetenido").GetDecimal());
        var movimientos = resultado.GetProperty("movimientos").EnumerateArray().ToList();
        Assert.Equal(13, movimientos.Count);
        Assert.Equal(113, movimientos[0].GetProperty("idMovimiento").GetInt64());
        Assert.Equal("Gabriel Andrés Pineda Rojas", Nombre(movimientos, 101));
        Assert.Equal("María José López Muñoz", Nombre(movimientos, 102));
        Assert.Equal("Gabriel Andrés Pineda Rojas", Nombre(movimientos, 113));
        foreach (var id in Enumerable.Range(103, 10)) Assert.Null(Nombre(movimientos, id));
        Assert.DoesNotContain(movimientos, m => m.GetProperty("idMovimiento").GetInt64() == 909);
        Assert.DoesNotContain("Persona ajena", resultado.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Alias de wallet", resultado.GetRawText(), StringComparison.Ordinal);
        Assert.All(movimientos, m => Assert.Equal(new[] {
            "descripcion", "fecha", "idMovimiento", "naturaleza", "nombreContraparte", "referenciaId", "referenciaTipo",
            "saldoAntes", "saldoDespues", "tipoMovimiento", "valor"
        }, m.EnumerateObject().Select(p => p.Name).OrderBy(p => p)));
        Assert.Equal(4, lecturas.Comandos.Count); // wallet + saldo + movimientos + un lote de nombres
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.All(lecturas.Comandos, q => Assert.DoesNotContain("[usuarios]", q, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sin_transferencias_wallet_no_consulta_nombres_y_conserva_descripcion()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new EsquemaPropio(cs);
        await s.IniciarAsync();
        await s.SembrarAsync();
        var lecturas = new LecturasPropias(s.Esquema);
        await using var db = s.Contexto(lecturas);

        var resultado = JsonSerializer.SerializeToElement(await new ReportesService(db).GetEstadoCuentaWalletAsync(10));

        var movimiento = Assert.Single(resultado.GetProperty("movimientos").EnumerateArray());
        Assert.Equal("Recarga original", movimiento.GetProperty("descripcion").GetString());
        Assert.Equal(JsonValueKind.Null, movimiento.GetProperty("nombreContraparte").ValueKind);
        Assert.Equal(3, lecturas.Comandos.Count);
        Assert.All(lecturas.Comandos, q => Assert.DoesNotContain("[personas]", q, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static string? Nombre(IEnumerable<JsonElement> movimientos, long id) => movimientos
        .Single(m => m.GetProperty("idMovimiento").GetInt64() == id).GetProperty("nombreContraparte").GetString();

    private static bool Conexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? "";
        if (string.IsNullOrWhiteSpace(cs))
        {
            Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
                "ConnectionStrings__XpayConnection es obligatoria en CI para comprobar nombres en movimientos.");
            return false;
        }
        var b = new SqlConnectionStringBuilder(cs);
        var host = b.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase).Split(',')[0];
        Assert.True((host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1" or "[::1]")
            && b.InitialCatalog.StartsWith("XPAY_", StringComparison.OrdinalIgnoreCase),
            "Estas pruebas solo permiten una base ficticia XPAY_ en SQL loopback.");
        return true;
    }

    private sealed class EsquemaPropio(string cs) : IAsyncDisposable
    {
        public string Esquema { get; } = "xpay_mov_test_" + Guid.NewGuid().ToString("N");

        public async Task IniciarAsync()
        {
            await EjecutarAsync($"CREATE SCHEMA [{Esquema}];");
            await EjecutarAsync($"""
                CREATE TABLE [{Esquema}].[wallets](id_wallet BIGINT NOT NULL PRIMARY KEY,
                  id_unidad_negocio BIGINT NOT NULL, tipo_wallet NVARCHAR(30) NOT NULL,
                  id_persona BIGINT NULL, id_comercio BIGINT NULL, nombre_wallet NVARCHAR(200) NULL,
                  estado NVARCHAR(20) NOT NULL, fecha_creacion DATETIME2 NOT NULL, fecha_actualizacion DATETIME2 NULL);
                CREATE TABLE [{Esquema}].[wallet_saldos](id_wallet BIGINT NOT NULL PRIMARY KEY,
                  saldo_disponible DECIMAL(18,2) NOT NULL, saldo_retenido DECIMAL(18,2) NOT NULL,
                  saldo_transito DECIMAL(18,2) NOT NULL, saldo_contingencia DECIMAL(18,2) NOT NULL,
                  fecha_actualizacion DATETIME2 NOT NULL);
                CREATE TABLE [{Esquema}].[wallet_movimientos](id_movimiento_wallet BIGINT NOT NULL PRIMARY KEY,
                  id_wallet BIGINT NOT NULL, fecha_movimiento DATETIME2 NOT NULL,
                  tipo_movimiento NVARCHAR(40) NOT NULL, naturaleza NVARCHAR(1) NOT NULL, valor DECIMAL(18,2) NOT NULL,
                  saldo_antes DECIMAL(18,2) NULL, saldo_despues DECIMAL(18,2) NULL, descripcion NVARCHAR(200) NULL,
                  referencia_tipo NVARCHAR(50) NULL, referencia_id BIGINT NULL);
                CREATE TABLE [{Esquema}].[personas](id_persona BIGINT NOT NULL PRIMARY KEY,
                  identidad_verificada BIT NOT NULL, nombre_verificado_completo NVARCHAR(200) NULL,
                  apellido_verificado_completo NVARCHAR(200) NULL, primer_nombre NVARCHAR(100) NULL,
                  segundo_nombre NVARCHAR(100) NULL, primer_apellido NVARCHAR(100) NULL, segundo_apellido NVARCHAR(100) NULL);
                """);
        }

        public async Task SembrarAsync()
        {
            await EjecutarAsync($"""
                INSERT INTO [{Esquema}].[wallets]
                  (id_wallet,id_unidad_negocio,tipo_wallet,id_persona,nombre_wallet,estado,fecha_creacion)
                VALUES (1,1,'PERSONA',1,'Wallet propia','ACTIVA',GETUTCDATE()),
                  (2,1,'PERSONA',2,'Alias de wallet','INACTIVA',GETUTCDATE()),
                  (3,1,'PERSONA',3,'Alias de wallet','INACTIVA',GETUTCDATE()),
                  (4,1,'COMERCIO',4,'Alias de wallet','ACTIVA',GETUTCDATE()),
                  (5,1,'PERSONA',NULL,'Alias de wallet','ACTIVA',GETUTCDATE()),
                  (6,1,'PERSONA',6,'Alias de wallet','ACTIVA',GETUTCDATE()),
                  (8,1,'PERSONA',8,'Alias de wallet','ACTIVA',GETUTCDATE()),
                  (9,1,'PERSONA',9,'Wallet ajena','ACTIVA',GETUTCDATE()),
                  (10,1,'PERSONA',10,'Solo recarga','ACTIVA',GETUTCDATE());
                INSERT INTO [{Esquema}].[wallet_saldos] VALUES (1,1234,50,0,0,GETUTCDATE());
                INSERT INTO [{Esquema}].[personas]
                  (id_persona,identidad_verificada,nombre_verificado_completo,apellido_verificado_completo,
                   primer_nombre,segundo_nombre,primer_apellido,segundo_apellido)
                VALUES (2,0,'No verificado','Ignorar',' Gabriel ','Andrés','Pineda','Rojas'),
                  (3,1,' María  José ',' López Muñoz ','Perfil','Distinto','No','Usar'),
                  (4,0,NULL,NULL,'Comercio','No personal',NULL,NULL),
                  (6,0,NULL,NULL,NULL,NULL,'Solo apellido',NULL),
                  (8,0,NULL,NULL,'Persona ajena',NULL,'No visible',NULL);
                INSERT INTO [{Esquema}].[wallet_movimientos]
                  (id_movimiento_wallet,id_wallet,fecha_movimiento,tipo_movimiento,naturaleza,valor,
                   saldo_antes,saldo_despues,descripcion,referencia_tipo,referencia_id)
                SELECT n,1,DATEADD(SECOND,n,'2026-01-01'),'TRANSFERENCIA_SALIDA','D',10,1244,1234,
                  'Descripción original','wallets',2 FROM (VALUES (101),(113)) ids(n);
                INSERT INTO [{Esquema}].[wallet_movimientos]
                  (id_movimiento_wallet,id_wallet,fecha_movimiento,tipo_movimiento,naturaleza,valor,
                   saldo_antes,saldo_despues,descripcion,referencia_tipo,referencia_id)
                SELECT n,1,DATEADD(SECOND,n,'2026-01-01'),tipo,'C',10,1224,1234,
                  'Descripción original',referencia,id FROM (VALUES
                  (102,'TRANSFERENCIA_ENTRADA','wallets',3),
                  (103,'TRANSFERENCIA_ENTRADA','wallets',4),
                  (104,'TRANSFERENCIA_ENTRADA','wallets',5),
                  (105,'TRANSFERENCIA_ENTRADA','wallets',999),
                  (106,'TRANSFERENCIA_ENTRADA','wallets',6),
                  (107,'RECARGA_WALLET','wallets',2),
                  (108,'TRANSFERENCIA_ENTRADA','ventas_qr',2),
                  (109,'TRANSFERENCIA_ENTRADA','wallets',0),
                  (110,'TRANSFERENCIA_ENTRADA',NULL,2),
                  (111,'TRANSFERENCIA_ENTRADA','wallets',-1),
                  (112,'TRANSFERENCIA_ENTRADA','wallets',NULL)) ids(n,tipo,referencia,id);
                INSERT INTO [{Esquema}].[wallet_movimientos]
                  (id_movimiento_wallet,id_wallet,fecha_movimiento,tipo_movimiento,naturaleza,valor,descripcion,referencia_tipo,referencia_id)
                VALUES (909,9,GETUTCDATE(),'TRANSFERENCIA_ENTRADA','C',7,'Historial ajeno','wallets',8),
                  (1001,10,GETUTCDATE(),'RECARGA_WALLET','C',9,'Recarga original','wallets',2);
                """);
        }

        public XpayDbContext Contexto(params IInterceptor[] interceptores) => new ContextoEsquema(
            new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs)
                .ReplaceService<IModelCacheKeyFactory, ClaveModeloEsquema>().AddInterceptors(interceptores).Options, Esquema);

        private async Task EjecutarAsync(string sql)
        {
            await using var c = new SqlConnection(cs);
            await c.OpenAsync();
            await using var q = c.CreateCommand();
            q.CommandText = sql;
            q.CommandTimeout = 30;
            await q.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync() => await EjecutarAsync($"""
            DROP TABLE IF EXISTS [{Esquema}].[wallet_movimientos];
            DROP TABLE IF EXISTS [{Esquema}].[wallet_saldos];
            DROP TABLE IF EXISTS [{Esquema}].[wallets];
            DROP TABLE IF EXISTS [{Esquema}].[personas];
            IF SCHEMA_ID(N'{Esquema}') IS NOT NULL DROP SCHEMA [{Esquema}];
            """);
    }

    private sealed class ContextoEsquema(DbContextOptions<XpayDbContext> options, string esquema) : XpayDbContext(options)
    {
        public string Esquema { get; } = esquema;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Wallet>().ToTable("wallets", Esquema);
            modelBuilder.Entity<WalletSaldo>().ToTable("wallet_saldos", Esquema);
            modelBuilder.Entity<WalletMovimiento>().ToTable("wallet_movimientos", Esquema);
            modelBuilder.Entity<Persona>().ToTable("personas", Esquema);
        }
    }

    public sealed class ClaveModeloEsquema : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is ContextoEsquema c
            ? (context.GetType(), c.Esquema, designTime) : (object)(context.GetType(), designTime);
        public object Create(DbContext context) => Create(context, false);
    }

    private sealed class LecturasPropias(string esquema) : DbCommandInterceptor
    {
        public List<string> Comandos { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Assert.StartsWith("SELECT", command.CommandText.TrimStart(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"[{esquema}].", command.CommandText, StringComparison.Ordinal);
            Comandos.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException("El estado de cuenta intentó escribir.");
    }
}
