using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Piloto PROD — corrección 3, integración SQL de SolicitarRetiroRealAsync en
// ambiente "Production": interruptor, tope de 1.000 UVB y límite diario
// interno XPAY bajo concurrencia. Solo reserva (no llama a Passport: el envío
// es EnviarPaymentAsync, que estas pruebas no invocan). Guard fail-closed del
// repo; colección SqlIntegration.
[Collection("SqlIntegration")]
public sealed class RetiroLimitesPilotoSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const decimal SaldoInicial = 2_000_000m;

    private static string AnioColombia => ColombiaTime.DesdeUtc(DateTime.UtcNow).Year.ToString();

    private static Dictionary<string, string?> ConfigPiloto(string uvb = "11000") => new()
    {
        [ControlOperacionesFinancieras.ClaveInterruptor] = "true",
        [ControlOperacionesFinancieras.ClaveUvbValor]    = uvb,
        [ControlOperacionesFinancieras.ClaveUvbAnio]     = AnioColombia,
        [ControlOperacionesFinancieras.ClaveTopeDiario]  = "500000",
    };

    [Fact]
    public async Task Dos_retiros_simultaneos_no_superan_el_limite_diario()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var s = await SembrarAsync(cs, c);

            // 300.000 + 300.000 > 500.000: solo uno puede pasar.
            var resultados = await Task.WhenAll(
                Task.Run(() => IntentarAsync(cs, s, 300_000m, ConfigPiloto())),
                Task.Run(() => IntentarAsync(cs, s, 300_000m, ConfigPiloto())));

            Assert.Single(resultados, r => r is null);
            Assert.Single(resultados, r => r == MotivoBloqueoFinanciero.LimiteDiarioXpay);

            await using var v = NuevoContexto(cs);
            var saldo = await v.WalletSaldos.AsNoTracking().SingleAsync(x => x.IdWallet == s.IdWallet);
            Assert.Equal(SaldoInicial - 300_000m, saldo.SaldoDisponible);
            Assert.Equal(300_000m, saldo.SaldoRetenido);
            Assert.Equal(1, await v.PassportBrebRetiros.CountAsync(r => r.IdWallet == s.IdWallet));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Cinco_retiros_simultaneos_de_150000_dejan_pasar_exactamente_tres()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var s = await SembrarAsync(cs, c);

            var resultados = await Task.WhenAll(Enumerable.Range(0, 5)
                .Select(_ => Task.Run(() => IntentarAsync(cs, s, 150_000m, ConfigPiloto()))));

            Assert.Equal(3, resultados.Count(r => r is null));
            Assert.Equal(2, resultados.Count(r => r == MotivoBloqueoFinanciero.LimiteDiarioXpay));

            await using var v = NuevoContexto(cs);
            Assert.Equal(450_000m, await v.PassportBrebRetiros.Where(r => r.IdWallet == s.IdWallet).SumAsync(r => r.Valor));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Rechazados_errores_y_dias_anteriores_no_cuentan_pero_envio_incierto_si()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var s = await SembrarAsync(cs, c);
            var hoy = DateTime.UtcNow;
            var ayer = ControlOperacionesFinancieras.InicioDiaColombiaUtc(hoy).AddMinutes(-1);
            await SembrarRetiroAsync(cs, s, 400_000m, "RECHAZADO", hoy);
            await SembrarRetiroAsync(cs, s, 400_000m, "ERROR", hoy);
            await SembrarRetiroAsync(cs, s, 500_000m, "LIQUIDADO", ayer);

            Assert.Null(await IntentarAsync(cs, s, 300_000m, ConfigPiloto()));

            // 300.000 reservado (PENDIENTE) + incierto de 150.000 = 450.000; 100.000 más lo supera.
            await SembrarRetiroAsync(cs, s, 150_000m, "ENVIADO_PASSPORT", hoy);
            Assert.Equal(MotivoBloqueoFinanciero.LimiteDiarioXpay, await IntentarAsync(cs, s, 100_000m, ConfigPiloto()));
            Assert.Null(await IntentarAsync(cs, s, 50_000m, ConfigPiloto()));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Tope_de_1000_UVB_por_operacion()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var s = await SembrarAsync(cs, c);
            // UVB sintética de 300 → tope por operación 300.000.
            Assert.Equal(MotivoBloqueoFinanciero.TopeOperacionUvb, await IntentarAsync(cs, s, 300_001m, ConfigPiloto(uvb: "300")));
            Assert.Null(await IntentarAsync(cs, s, 300_000m, ConfigPiloto(uvb: "300")));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Pausa_en_produccion_no_reserva_ni_crea_retiro()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var s = await SembrarAsync(cs, c);
            var config = ConfigPiloto();
            config.Remove(ControlOperacionesFinancieras.ClaveInterruptor);

            Assert.Equal(MotivoBloqueoFinanciero.Pausa, await IntentarAsync(cs, s, 10_000m, config));

            await using var v = NuevoContexto(cs);
            var saldo = await v.WalletSaldos.AsNoTracking().SingleAsync(x => x.IdWallet == s.IdWallet);
            Assert.Equal(SaldoInicial, saldo.SaldoDisponible);
            Assert.Equal(0m, saldo.SaldoRetenido);
            Assert.Equal(0, await v.PassportBrebRetiros.CountAsync(r => r.IdWallet == s.IdWallet));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record Sembrado(long IdPersona, long IdUsuario, long IdWallet, long IdLlave);

    private sealed class Creados
    {
        public List<long> Personas { get; } = [];
        public List<long> Usuarios { get; } = [];
        public List<long> Wallets  { get; } = [];
    }

    // null = retiro reservado; si no, el motivo del bloqueo.
    private static async Task<MotivoBloqueoFinanciero?> IntentarAsync(
        string cs, Sembrado s, decimal monto, Dictionary<string, string?> valores)
    {
        await using var db = NuevoContexto(cs);
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var svc = new BrebPaymentService(db, new FakePassportPaymentClient(), config,
            NullLogger<BrebPaymentService>.Instance,
            new ControlOperacionesFinancieras(config, AmbienteXpayTests.Ambiente("Production")));
        try
        {
            await svc.SolicitarRetiroRealAsync(s.IdPersona, s.IdUsuario, monto);
            return null;
        }
        catch (OperacionFinancieraBloqueadaException ex) { return ex.Motivo; }
    }

    private static async Task<Sembrado> SembrarAsync(string cs, Creados c)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, Celular = "3000000000", Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        c.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"retlim_{sufijo}", PasswordHash = "x",
            Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        c.Usuarios.Add(usuario.IdUsuario);

        var wallet = new Wallet
        {
            IdUnidadNegocio = idUnidad, TipoWallet = "PERSONA", IdPersona = persona.IdPersona,
            NombreWallet = $"w_{sufijo}", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Wallets.Add(wallet);
        await ctx.SaveChangesAsync();
        c.Wallets.Add(wallet.IdWallet);
        ctx.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, SaldoDisponible = SaldoInicial, FechaActualizacion = ahora });

        // Llave resuelta por Passport (OwnerNameMasked) con resolución vigente.
        var llave = new PassportBrebLlave
        {
            TipoSujeto = "USUARIO", IdUsuario = usuario.IdUsuario, IdWallet = wallet.IdWallet,
            KeyType = "BCODE", KeyValueMasked = "****1234", KeyValueHash = $"h_{sufijo}",
            Estado = "VALIDADA", FechaRegistro = ahora, EsActiva = true,
            OwnerNameMasked = "P*** S***", PassportResolutionId = $"res_{sufijo}",
            PassportResolutionExpiresAtUtc = ahora.AddHours(1),
        };
        ctx.PassportBrebLlaves.Add(llave);
        await ctx.SaveChangesAsync();
        return new Sembrado(persona.IdPersona, usuario.IdUsuario, wallet.IdWallet, llave.IdBrebLlave);
    }

    // Retiro histórico sintético (sin ledger): solo cuenta para el acumulado.
    private static async Task SembrarRetiroAsync(string cs, Sembrado s, decimal valor, string estado, DateTime fechaUtc)
    {
        await using var ctx = NuevoContexto(cs);
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        ctx.PassportBrebRetiros.Add(new PassportBrebRetiro
        {
            TipoSujeto = "USUARIO", IdUsuario = s.IdUsuario, IdWallet = s.IdWallet, IdBrebLlave = s.IdLlave,
            Valor = valor, Moneda = "COP", Estado = estado,
            ReferenciaInterna = $"HIST{sufijo}".ToUpperInvariant(), IdempotencyKey = $"HIST-{sufijo}",
            FechaSolicitud = fechaUtc, CreatedByUsuario = s.IdUsuario,
        });
        await ctx.SaveChangesAsync();
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de límites de retiro.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Creados c)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var w in c.Wallets)
            {
                var txs = await ctx.PassportBrebRetiros.AsNoTracking()
                    .Where(r => r.IdWallet == w && r.IdTransaccionLedger != null)
                    .Select(r => r.IdTransaccionLedger!.Value).ToListAsync();
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_movimientos WHERE id_wallet = {w}");
                await ctx.Database.ExecuteSqlAsync($"UPDATE dbo.passport_breb_retiros SET id_transaccion_ledger = NULL WHERE id_wallet = {w}");
                foreach (var t in txs)
                {
                    await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.ledger_movimientos WHERE id_transaccion_ledger = {t}");
                    await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.ledger_transacciones WHERE id_transaccion_ledger = {t}");
                }
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.passport_breb_retiros WHERE id_wallet = {w}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.passport_breb_llaves WHERE id_wallet = {w}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {w}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {w}");
            }
            foreach (var u in c.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {u}");
            foreach (var p in c.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {p}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RetiroLimitesPilotoSqlTests] cleanup parcial falló ({ex.GetType().Name}).");
        }
    }
}
