using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Verificación del destinatario contra SQL Server local: la vista previa
// (DestinatarioWalletService.ResolverAsync), la comprobación dentro de
// TransferirWalletAsync y el bloqueo que la protege de cambios concurrentes.
// Cada prueba siembra sus propias personas/usuarios/wallets y las borra al
// terminar.
[Collection("SqlIntegration")]
public sealed class DestinatarioWalletSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const decimal SaldoInicial = 100_000m;

    [Fact]
    public async Task Resolver_DestinoValido_DevuelveUsuarioYNombreEnmascarado()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen  = await TitularAsync(cs, s);
            var destino = await TitularAsync(cs, s, primerNombre: "Destino Prueba", primerApellido: "sintetico");

            await using var ctx = NuevoContexto(cs);
            var r = await new DestinatarioWalletService(ctx).ResolverAsync(destino.IdWallet, origen.IdPersona);

            Assert.NotNull(r);
            Assert.Equal(destino.IdWallet, r.IdWallet);
            Assert.Equal(destino.Usuario, r.Usuario);
            Assert.Equal("Destino S.", r.NombreVisible);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Resolver_DestinoNoValido_DevuelveNullSinDistinguirMotivo()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen          = await TitularAsync(cs, s);
            var walletInactiva  = await TitularAsync(cs, s, estadoWallet: "INACTIVA");
            var usuarioInactivo = await TitularAsync(cs, s, estadoUsuario: EstadosUsuario.Inactivo);
            var dosUsuarios     = await TitularAsync(cs, s);
            await SegundoUsuarioActivoAsync(cs, s, dosUsuarios.IdPersona);

            await using var ctx = NuevoContexto(cs);
            var svc = new DestinatarioWalletService(ctx);
            Assert.Null(await svc.ResolverAsync(long.MaxValue, origen.IdPersona));
            Assert.Null(await svc.ResolverAsync(walletInactiva.IdWallet, origen.IdPersona));
            Assert.Null(await svc.ResolverAsync(usuarioInactivo.IdWallet, origen.IdPersona));
            Assert.Null(await svc.ResolverAsync(dosUsuarios.IdWallet, origen.IdPersona));
            Assert.Null(await svc.ResolverAsync(origen.IdWallet, origen.IdPersona)); // la propia
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Transferir_DestinatarioConfirmado_MueveSaldo()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen  = await TitularAsync(cs, s, saldo: SaldoInicial);
            var destino = await TitularAsync(cs, s);

            await using (var ctx = NuevoContexto(cs))
            {
                var r = await Servicio(ctx).TransferirWalletAsync(origen.IdWallet, origen.IdUsuario, Guid.NewGuid(),
                    Request(destino, 25_000m, destino.Usuario));
                Assert.False(r.Replayed);
            }

            Assert.Equal(SaldoInicial - 25_000m, await SaldoAsync(cs, origen.IdWallet));
            Assert.Equal(25_000m, await SaldoAsync(cs, destino.IdWallet));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Transferir_DestinatarioNoCoincide_SeRechazaSinMoverDinero()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen  = await TitularAsync(cs, s, saldo: SaldoInicial);
            var destino = await TitularAsync(cs, s);
            var otro    = await TitularAsync(cs, s);

            await using (var ctx = NuevoContexto(cs))
                await Assert.ThrowsAsync<DestinatarioNoCoincideException>(() =>
                    Servicio(ctx).TransferirWalletAsync(origen.IdWallet, origen.IdUsuario, Guid.NewGuid(),
                        Request(destino, 25_000m, otro.Usuario)));

            await SinEfectosAsync(cs, origen, destino);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Transferir_SinDestinatarioEsperado_SeRechazaSinMoverDinero(string? esperado)
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen  = await TitularAsync(cs, s, saldo: SaldoInicial);
            var destino = await TitularAsync(cs, s);

            await using (var ctx = NuevoContexto(cs))
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    Servicio(ctx).TransferirWalletAsync(origen.IdWallet, origen.IdUsuario, Guid.NewGuid(),
                        Request(destino, 25_000m, esperado)));
                Assert.Equal("Falta confirmar el destinatario.", ex.Message);
            }

            await SinEfectosAsync(cs, origen, destino);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Transferir_TitularDesactivadoTrasConfirmar_SeRechazaSinMoverDinero()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen  = await TitularAsync(cs, s, saldo: SaldoInicial);
            var destino = await TitularAsync(cs, s);

            await using (var ctx = NuevoContexto(cs))
            {
                // El ordenante confirma; después, antes de transferir, el titular se desactiva.
                var confirmado = await new DestinatarioWalletService(ctx).ResolverAsync(destino.IdWallet, origen.IdPersona);
                Assert.NotNull(confirmado);
                await ctx.Usuarios.Where(u => u.IdUsuario == destino.IdUsuario)
                    .ExecuteUpdateAsync(set => set.SetProperty(u => u.Estado, EstadosUsuario.Inactivo));

                await Assert.ThrowsAsync<DestinatarioNoCoincideException>(() =>
                    Servicio(ctx).TransferirWalletAsync(origen.IdWallet, origen.IdUsuario, Guid.NewGuid(),
                        Request(destino, 25_000m, confirmado.Usuario)));
            }

            await SinEfectosAsync(cs, origen, destino);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // Mientras la transacción de la transferencia retiene el bloqueo del
    // destinatario, otra sesión no puede desactivar al titular, desactivar
    // la wallet ni añadir otro usuario a esa persona (LOCK_TIMEOUT → 1222).
    [Fact]
    public async Task ResolverConBloqueo_RetieneTitularYWalletHastaElFinDeLaTransaccion()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen  = await TitularAsync(cs, s);
            var destino = await TitularAsync(cs, s);

            await using var ctxTransferencia = NuevoContexto(cs);
            await using var tx = await ctxTransferencia.Database.BeginTransactionAsync();
            var titular = await new DestinatarioWalletService(ctxTransferencia)
                .ResolverConBloqueoAsync(destino.IdWallet, origen.IdPersona);
            Assert.Equal(destino.Usuario, titular?.Usuario);

            await using (var otra = NuevoContexto(cs))
            {
                await otra.Database.OpenConnectionAsync();
                await otra.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 1000");

                await EsperaBloqueoAsync(() => otra.Database.ExecuteSqlAsync(
                    $"UPDATE usuarios SET estado = {EstadosUsuario.Inactivo} WHERE id_usuario = {destino.IdUsuario}"));
                await EsperaBloqueoAsync(() => otra.Database.ExecuteSqlAsync(
                    $"UPDATE wallets SET estado = 'INACTIVA' WHERE id_wallet = {destino.IdWallet}"));

                otra.Usuarios.Add(NuevoUsuario(destino.IdPersona, "dws_extra"));
                var ex = await Assert.ThrowsAsync<DbUpdateException>(() => otra.SaveChangesAsync());
                Assert.Equal(1222, Assert.IsType<SqlException>(ex.InnerException).Number);
            }

            await tx.RollbackAsync();
            Assert.Equal(EstadosUsuario.Activo, await EstadoUsuarioAsync(cs, destino.IdUsuario));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private sealed record Titular(long IdPersona, long IdUsuario, string Usuario, long IdWallet);

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets  { get; } = new();
    }

    private static WalletOperacionService Servicio(XpayDbContext ctx) => new(ctx, new DestinatarioWalletService(ctx));

    private static TransferenciaWalletRequest Request(Titular destino, decimal valor, string? esperado) => new()
    {
        IdWalletDestino = destino.IdWallet, Valor = valor,
        Descripcion = "Transferencia prueba destinatario", UsuarioDestinoEsperado = esperado,
    };

    private static async Task EsperaBloqueoAsync(Func<Task> accion)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(accion);
        Assert.Equal(1222, ex.Number);
    }

    private static async Task SinEfectosAsync(string cs, Titular origen, Titular destino)
    {
        Assert.Equal(SaldoInicial, await SaldoAsync(cs, origen.IdWallet));
        Assert.Equal(0m, await SaldoAsync(cs, destino.IdWallet));
        await using var ctx = NuevoContexto(cs);
        Assert.Equal(0, await ctx.WalletMovimientos.AsNoTracking()
            .CountAsync(m => m.IdWallet == origen.IdWallet || m.IdWallet == destino.IdWallet));
        Assert.Equal(0, await ctx.LedgerTransacciones.AsNoTracking()
            .CountAsync(t => t.ReferenciaTipo == "wallets" && t.ReferenciaId == origen.IdWallet));
        Assert.Equal(0, await ctx.WalletIdempotencias.AsNoTracking()
            .CountAsync(i => i.IdUsuario == origen.IdUsuario && i.Endpoint == IdempotencyEndpoints.WalletsTransferencia));
    }

    private static async Task<decimal> SaldoAsync(string cs, long idWallet)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.WalletSaldos.AsNoTracking().Where(w => w.IdWallet == idWallet)
            .Select(w => w.SaldoDisponible).SingleAsync();
    }

    private static async Task<string> EstadoUsuarioAsync(string cs, long idUsuario)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Usuarios.AsNoTracking().Where(u => u.IdUsuario == idUsuario).Select(u => u.Estado).SingleAsync();
    }

    private static Usuario NuevoUsuario(long idPersona, string prefijo) => new()
    {
        IdPersona = idPersona, NombreUsuario = $"{prefijo}_{Guid.NewGuid().ToString("N")[..12]}",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
        Estado = EstadosUsuario.Activo, FechaCreacion = DateTime.UtcNow,
    };

    private static async Task<Titular> TitularAsync(
        string cs, Sembrados s, string estadoUsuario = EstadosUsuario.Activo, string estadoWallet = "ACTIVA",
        decimal saldo = 0m, string primerNombre = "DWS", string primerApellido = "Sintetico")
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"DWS{sufijo}",
            PrimerNombre = primerNombre, PrimerApellido = primerApellido, Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        s.Personas.Add(persona.IdPersona);

        var usuario = NuevoUsuario(persona.IdPersona, "dws");
        usuario.Estado = estadoUsuario;
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        s.Usuarios.Add(usuario.IdUsuario);

        var wallet = new Wallet
        {
            IdUnidadNegocio = idUnidad, TipoWallet = "PERSONA", IdPersona = persona.IdPersona,
            NombreWallet = $"w_{sufijo}", Estado = estadoWallet, FechaCreacion = ahora,
        };
        ctx.Wallets.Add(wallet);
        await ctx.SaveChangesAsync();
        s.Wallets.Add(wallet.IdWallet);

        ctx.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, SaldoDisponible = saldo, FechaActualizacion = ahora });
        await ctx.SaveChangesAsync();
        return new Titular(persona.IdPersona, usuario.IdUsuario, usuario.NombreUsuario, wallet.IdWallet);
    }

    private static async Task SegundoUsuarioActivoAsync(string cs, Sembrados s, long idPersona)
    {
        await using var ctx = NuevoContexto(cs);
        var usuario = NuevoUsuario(idPersona, "dws2");
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        s.Usuarios.Add(usuario.IdUsuario);
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL del destinatario.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Solo filas creadas por la prueba, en orden de dependencias.
    private static async Task LimpiarAsync(string cs, Sembrados s)
    {
        if (string.IsNullOrWhiteSpace(cs)) return;
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in s.Wallets)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_movimientos WHERE id_wallet = {id}");
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.ledger_movimientos WHERE id_transaccion_ledger IN (SELECT id_transaccion_ledger FROM dbo.ledger_transacciones WHERE referencia_tipo = 'wallets' AND referencia_id = {id})");
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.ledger_transacciones WHERE referencia_tipo = 'wallets' AND referencia_id = {id}");
            }
            foreach (var id in s.Usuarios)
            {
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.wallet_idempotencia WHERE id_usuario = {id} AND endpoint = {IdempotencyEndpoints.WalletsTransferencia}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.auditoria WHERE id_usuario = {id}");
            }
            foreach (var id in s.Wallets)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
            }
            foreach (var id in s.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in s.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DestinatarioWalletSqlTests] limpieza parcial falló ({ex.GetType().Name}).");
        }
    }
}
