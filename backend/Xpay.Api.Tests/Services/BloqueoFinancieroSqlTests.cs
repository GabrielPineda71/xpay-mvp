using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Bloqueo financiero, fase visual, contra SQL Server (obligatorio en CI): con
// BloqueoFinancieroInterceptor y el interruptor cerrado en un ambiente
// productivo, una transferencia o un cupo nuevo se rechazan y NO cambian
// saldos, movimientos, ledger, idempotencia ni cupos; el alta de una wallet
// con saldo 0 sigue permitida y, con el interruptor abierto, la transferencia
// funciona. Cada prueba siembra y borra sus propias filas.
[Collection("SqlIntegration")]
public sealed class BloqueoFinancieroSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const decimal SaldoInicial = 100_000m;

    [Fact]
    public async Task Transferencia_InterruptorCerrado_NoCambiaNada()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen  = await TitularAsync(cs, s, SaldoInicial);
            var destino = await TitularAsync(cs, s);

            await using (var ctx = NuevoContexto(cs, ControlesFinancierosPrueba.CerradoEnProduccion()))
                await Assert.ThrowsAsync<OperacionFinancieraBloqueadaException>(() =>
                    Servicio(ctx).TransferirWalletAsync(origen.IdWallet, origen.IdUsuario, Guid.NewGuid(), Request(destino)));

            Assert.Equal(SaldoInicial, await SaldoAsync(cs, origen.IdWallet));
            Assert.Equal(0m, await SaldoAsync(cs, destino.IdWallet));
            await using var lectura = NuevoContexto(cs);
            Assert.Equal(0, await lectura.WalletMovimientos.CountAsync(m => m.IdWallet == origen.IdWallet || m.IdWallet == destino.IdWallet));
            Assert.Equal(0, await lectura.LedgerTransacciones.CountAsync(t => t.ReferenciaTipo == "wallets" && t.ReferenciaId == origen.IdWallet));
            Assert.Equal(0, await lectura.WalletIdempotencias.CountAsync(i => i.IdUsuario == origen.IdUsuario));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Transferencia_InterruptorAbierto_SigueFuncionando()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var origen  = await TitularAsync(cs, s, SaldoInicial);
            var destino = await TitularAsync(cs, s);

            await using (var ctx = NuevoContexto(cs, ControlesFinancierosPrueba.AbiertoEnProduccion()))
                await Servicio(ctx).TransferirWalletAsync(origen.IdWallet, origen.IdUsuario, Guid.NewGuid(), Request(destino));

            Assert.Equal(SaldoInicial - 25_000m, await SaldoAsync(cs, origen.IdWallet));
            Assert.Equal(25_000m, await SaldoAsync(cs, destino.IdWallet));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task CupoNuevo_InterruptorCerrado_NoSeCrea()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var titular = await TitularAsync(cs, s);

            await using (var ctx = NuevoContexto(cs, ControlesFinancierosPrueba.CerradoEnProduccion()))
            {
                ctx.CarteraCuposOrdinarios.Add(new CarteraCupoOrdinario
                {
                    IdUsuario = titular.IdUsuario, IdWallet = titular.IdWallet, CupoAprobado = 500_000m,
                    Estado = "ACTIVO", FechaAprobacion = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                });
                await Assert.ThrowsAsync<OperacionFinancieraBloqueadaException>(() => ctx.SaveChangesAsync());
            }

            await using var lectura = NuevoContexto(cs);
            Assert.Equal(0, await lectura.CarteraCuposOrdinarios.CountAsync(c => c.IdUsuario == titular.IdUsuario));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // Lo que necesita la fase visual (alta del piloto) sigue funcionando:
    // persona, usuario, wallet y saldo en 0 con el interruptor cerrado.
    [Fact]
    public async Task AltaDeWalletConSaldoCero_InterruptorCerrado_SePermite()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var titular = await TitularAsync(cs, s, control: ControlesFinancierosPrueba.CerradoEnProduccion());
            Assert.Equal(0m, await SaldoAsync(cs, titular.IdWallet));
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

    private static TransferenciaWalletRequest Request(Titular destino) => new()
    {
        IdWalletDestino = destino.IdWallet, Valor = 25_000m,
        Descripcion = "Transferencia prueba bloqueo", UsuarioDestinoEsperado = destino.Usuario,
    };

    private static async Task<decimal> SaldoAsync(string cs, long idWallet)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.WalletSaldos.AsNoTracking().Where(w => w.IdWallet == idWallet)
            .Select(w => w.SaldoDisponible).SingleAsync();
    }

    // Sembrado: el saldo inicial (si lo hay) se escribe SIN interceptor; con
    // `control`, toda el alta pasa por el interceptor (saldo 0).
    private static async Task<Titular> TitularAsync(
        string cs, Sembrados s, decimal saldo = 0m, ControlOperacionesFinancieras? control = null)
    {
        await using var ctx = NuevoContexto(cs, control);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"BFS{sufijo}",
            PrimerNombre = "BFS", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        s.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"bfs_{sufijo}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
            Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        s.Usuarios.Add(usuario.IdUsuario);

        var wallet = new Wallet
        {
            IdUnidadNegocio = idUnidad, TipoWallet = "PERSONA", IdPersona = persona.IdPersona,
            NombreWallet = $"w_{sufijo}", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Wallets.Add(wallet);
        await ctx.SaveChangesAsync();
        s.Wallets.Add(wallet.IdWallet);

        ctx.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, SaldoDisponible = saldo, FechaActualizacion = ahora });
        await ctx.SaveChangesAsync();
        return new Titular(persona.IdPersona, usuario.IdUsuario, usuario.NombreUsuario, wallet.IdWallet);
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL del bloqueo financiero.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs, ControlOperacionesFinancieras? control = null)
    {
        var builder = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs);
        if (control is not null) builder.AddInterceptors(new BloqueoFinancieroInterceptor(control));
        return new XpayDbContext(builder.Options);
    }

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
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_cupos_ordinarios WHERE id_wallet = {id}");
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
            Console.Error.WriteLine($"[BloqueoFinancieroSqlTests] limpieza parcial falló ({ex.GetType().Name}).");
        }
    }
}
