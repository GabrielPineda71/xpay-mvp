using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0 J.3 (RECHAZO_CONFIRMADO de envío incierto) y J.6 (huérfano
// PENDIENTE_ENVIO_PASSPORT). Mismo patrón SQL que el resto del repositorio:
// base real, se salta en local sin cadena de conexión, obligatoria en CI.
// Ningún test llama a Passport (FakePassportPaymentClient sin respuestas).
[Collection("SqlIntegration")]
public sealed class BrebEnvioInciertoResolucionTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const decimal Valor = 50_000m;
    private const decimal DisponibleInicial = 100_000m;
    private const string EvidenciaValida = "Ticket JSD-1234: Passport confirma que el pago no fue creado.";

    // ── J.3 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task J3_RechazoConfirmado_LiberaHeldUnaVez_YRegistraResolucion()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Incierto());

            var r = await Servicio(cs, svc => svc.ResolverRechazoConfirmadoAsync(s.IdRetiro, EvidenciaValida, 77));

            Assert.Equal("RECHAZADO", r.Estado);
            await using var v = NuevoContexto(cs);
            var retiro = await v.PassportBrebRetiros.AsNoTracking().SingleAsync(x => x.IdBrebRetiro == s.IdRetiro);
            Assert.Equal("RECHAZO_CONFIRMADO", retiro.ResolucionInciertaTipo);
            Assert.Equal(EvidenciaValida, retiro.ResolucionInciertaEvidencia);
            Assert.Equal(77, retiro.ResolucionInciertaPor);
            Assert.NotNull(retiro.ResolucionInciertaFecha);
            Assert.NotNull(retiro.FechaRechazo);
            await AssertLiberadoUnaVezAsync(cs, s, "BREB_RETIRO_REAL_RECHAZAR", "RETIRO_BREB_REAL_RECHAZADO");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task J3_Repeticion_EsIdempotente_SinSegundoMovimiento()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Incierto());
            await Servicio(cs, svc => svc.ResolverRechazoConfirmadoAsync(s.IdRetiro, EvidenciaValida, 77));
            var r2 = await Servicio(cs, svc => svc.ResolverRechazoConfirmadoAsync(s.IdRetiro, EvidenciaValida, 77));

            Assert.Equal("RECHAZADO", r2.Estado);
            await AssertLiberadoUnaVezAsync(cs, s, "BREB_RETIRO_REAL_RECHAZAR", "RETIRO_BREB_REAL_RECHAZADO");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    public static TheoryData<string, Envio> J3NoElegibles => new()
    {
        { "con payment_id",         Envio.Incierto() with { PaymentId = "pay-xyz" } },
        { "menos de 2 h",           Envio.Incierto() with { MinutosDesdeEnvio = 60 } },
        { "resolution vigente",     Envio.Incierto() with { MinutosHastaVencerResolucion = 10 } },
        { "resolution desconocida", Envio.Incierto() with { SinVencimientoResolucion = true } },
        { "estado PENDIENTE",       Envio.Incierto() with { Estado = "PENDIENTE_ENVIO_PASSPORT" } },
        { "estado LIQUIDADO",       Envio.Incierto() with { Estado = "LIQUIDADO" } },
        { "estado ERROR",           Envio.Incierto() with { Estado = "ERROR" } },
    };

    [Theory]
    [MemberData(nameof(J3NoElegibles))]
    public async Task J3_NoElegible_SeRechaza_SinMoverDinero(string caso, Envio envio)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, envio);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Servicio(cs, svc => svc.ResolverRechazoConfirmadoAsync(s.IdRetiro, EvidenciaValida, 77)));
            await AssertSinMovimientoAsync(cs, s, envio.Estado);
            Assert.NotNull(caso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corta")]
    public async Task J3_EvidenciaAusenteOCorta_SeRechaza(string? evidencia)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Incierto());
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Servicio(cs, svc => svc.ResolverRechazoConfirmadoAsync(s.IdRetiro, evidencia, 77)));
            await AssertSinMovimientoAsync(cs, s, "ENVIADO_PASSPORT");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task J3_Concurrencia_ExactamenteUnaLiberacion()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Incierto());
            await Task.WhenAll(
                Servicio(cs, svc => svc.ResolverRechazoConfirmadoAsync(s.IdRetiro, EvidenciaValida, 77)),
                Servicio(cs, svc => svc.ResolverRechazoConfirmadoAsync(s.IdRetiro, EvidenciaValida, 78)));
            await AssertLiberadoUnaVezAsync(cs, s, "BREB_RETIRO_REAL_RECHAZAR", "RETIRO_BREB_REAL_RECHAZADO");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── J.6 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task J6_HuerfanoMas15Min_LiberaUnaVez_QuedaErrorOrphan()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Huerfano(minutosDesdeSolicitud: 20));
            var r = await Servicio(cs, svc => svc.LiberarPendienteEnvioHuerfanoAsync(s.IdRetiro, 77));

            Assert.Equal("ERROR", r.Estado);
            Assert.Equal(BrebPaymentService.MotivoOrphanPendienteEnvio, r.MotivoRechazo);
            await AssertLiberadoUnaVezAsync(cs, s, "BREB_RETIRO_REAL_ERROR_LOCAL", "RETIRO_BREB_REAL_ERROR_LOCAL");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task J6_Menos15Min_NoLibera()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Huerfano(minutosDesdeSolicitud: 5));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Servicio(cs, svc => svc.LiberarPendienteEnvioHuerfanoAsync(s.IdRetiro, 77)));
            await AssertSinMovimientoAsync(cs, s, "PENDIENTE_ENVIO_PASSPORT");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task J6_Repeticion_SinSegundoEfecto()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Huerfano(minutosDesdeSolicitud: 20));
            await Servicio(cs, svc => svc.LiberarPendienteEnvioHuerfanoAsync(s.IdRetiro, 77));
            var r2 = await Servicio(cs, svc => svc.LiberarPendienteEnvioHuerfanoAsync(s.IdRetiro, 77));

            Assert.Equal("ERROR", r2.Estado);
            await AssertLiberadoUnaVezAsync(cs, s, "BREB_RETIRO_REAL_ERROR_LOCAL", "RETIRO_BREB_REAL_ERROR_LOCAL");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task J6_EnviadoPassport_NoEsElegible()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Incierto());
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Servicio(cs, svc => svc.LiberarPendienteEnvioHuerfanoAsync(s.IdRetiro, 77)));
            await AssertSinMovimientoAsync(cs, s, "ENVIADO_PASSPORT");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task J6_Concurrencia_ExactamenteUnaLiberacion()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarAsync(cs, creados, Envio.Huerfano(minutosDesdeSolicitud: 20));
            await Task.WhenAll(
                Servicio(cs, svc => svc.LiberarPendienteEnvioHuerfanoAsync(s.IdRetiro, 77)),
                Servicio(cs, svc => svc.LiberarPendienteEnvioHuerfanoAsync(s.IdRetiro, 78)));
            await AssertLiberadoUnaVezAsync(cs, s, "BREB_RETIRO_REAL_ERROR_LOCAL", "RETIRO_BREB_REAL_ERROR_LOCAL");
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Frontera / roles (sin base de datos) ───────────────────────────────

    [Theory]
    [InlineData(nameof(BrebController.ResolverRechazoConfirmado))]
    [InlineData(nameof(BrebController.LiberarPendienteEnvio))]
    public void Endpoints_RequierenAdminXpayOSuperusuario(string metodo)
    {
        var accion = Assert.Single(typeof(BrebController).GetMethods(BindingFlags.Public | BindingFlags.Instance),
                                   m => m.Name == metodo);
        var roles = accion.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Roles).ToList();
        Assert.Contains("ADMIN_XPAY,SUPERUSUARIO", roles);
    }

    // ── Utilidades ─────────────────────────────────────────────────────────

    public sealed record Envio(
        string Estado, int MinutosDesdeSolicitud, int MinutosDesdeEnvio,
        int MinutosHastaVencerResolucion, bool SinVencimientoResolucion, string? PaymentId)
    {
        // ENVIADO_PASSPORT sin payment_id, enviado hace 3 h, resolution vencida hace 2 h.
        public static Envio Incierto() => new("ENVIADO_PASSPORT", 180, 180, -120, false, null);
        public static Envio Huerfano(int minutosDesdeSolicitud) =>
            new("PENDIENTE_ENVIO_PASSPORT", minutosDesdeSolicitud, 0, 30, false, null);
    }

    private sealed record Sembrado(long IdRetiro, long IdWallet);

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets { get; } = new();
    }

    private static async Task<T> Servicio<T>(string cs, Func<BrebPaymentService, Task<T>> accion)
    {
        await using var ctx = NuevoContexto(cs);
        var svc = new BrebPaymentService(ctx, new FakePassportPaymentClient(),
            new ConfigurationBuilder().Build(), NullLogger<BrebPaymentService>.Instance);
        return await accion(svc);
    }

    private static async Task<Sembrado> SembrarAsync(string cs, Sembrados creados, Envio e)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
            NumeroDocumento = $"66{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
            PrimerNombre = "BrebRes", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"brebres_{sufijo}",
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);

        var wallet = new Wallet
        {
            IdUnidadNegocio = idUnidad, TipoWallet = "PERSONA", IdPersona = persona.IdPersona,
            NombreWallet = $"w_{sufijo}", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Wallets.Add(wallet);
        await ctx.SaveChangesAsync();
        creados.Wallets.Add(wallet.IdWallet);
        // Estado posterior a la reserva: Valor ya movido a Retenido.
        ctx.WalletSaldos.Add(new WalletSaldo
        {
            IdWallet = wallet.IdWallet, SaldoDisponible = DisponibleInicial, SaldoRetenido = Valor,
            FechaActualizacion = ahora,
        });

        var llave = new PassportBrebLlave
        {
            TipoSujeto = "USUARIO", IdUsuario = usuario.IdUsuario, IdWallet = wallet.IdWallet,
            KeyType = "BCODE", KeyValueMasked = "****1234", KeyValueHash = $"h_{sufijo}",
            Estado = "VALIDADA", FechaRegistro = ahora, EsActiva = true,
        };
        ctx.PassportBrebLlaves.Add(llave);
        await ctx.SaveChangesAsync();

        var retiro = new PassportBrebRetiro
        {
            TipoSujeto = "USUARIO", IdUsuario = usuario.IdUsuario, IdWallet = wallet.IdWallet,
            IdBrebLlave = llave.IdBrebLlave, Valor = Valor, Moneda = "COP", Estado = e.Estado,
            PassportPaymentId = e.PaymentId,
            PassportResolutionId = $"res_{sufijo}",
            PassportResolutionExpiresAtUtc = e.SinVencimientoResolucion ? null : ahora.AddMinutes(e.MinutosHastaVencerResolucion),
            ReferenciaInterna = $"REF{sufijo}".ToUpperInvariant(),
            IdempotencyKey = $"TEST-{sufijo}",
            FechaSolicitud = ahora.AddMinutes(-e.MinutosDesdeSolicitud),
            FechaEnvioPassport = e.Estado == "PENDIENTE_ENVIO_PASSPORT" ? null : ahora.AddMinutes(-e.MinutosDesdeEnvio),
            CreatedByUsuario = usuario.IdUsuario,
        };
        ctx.PassportBrebRetiros.Add(retiro);
        await ctx.SaveChangesAsync();
        return new Sembrado(retiro.IdBrebRetiro, wallet.IdWallet);
    }

    private static async Task AssertLiberadoUnaVezAsync(string cs, Sembrado s, string tipoTx, string tipoMov)
    {
        await using var v = NuevoContexto(cs);
        var saldo = await v.WalletSaldos.AsNoTracking().SingleAsync(x => x.IdWallet == s.IdWallet);
        Assert.Equal(DisponibleInicial + Valor, saldo.SaldoDisponible);
        Assert.Equal(0m, saldo.SaldoRetenido);

        var txs = await v.LedgerTransacciones.AsNoTracking()
            .Where(t => t.ReferenciaTipo == "passport_breb_retiros" && t.ReferenciaId == s.IdRetiro).ToListAsync();
        var tx = Assert.Single(txs);
        Assert.Equal(tipoTx, tx.TipoTransaccion);

        var movs = await v.LedgerMovimientos.AsNoTracking()
            .Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger)
            .Join(v.LedgerCuentas, m => m.IdCuenta, c => c.IdCuenta, (m, c) => new { c.Codigo, m.Naturaleza, m.Valor })
            .ToListAsync();
        Assert.Equal(2, movs.Count);
        Assert.Contains(movs, m => m.Codigo == "210204" && m.Naturaleza == "D" && m.Valor == Valor);
        Assert.Contains(movs, m => m.Codigo == "210101" && m.Naturaleza == "C" && m.Valor == Valor);

        var wmov = await v.WalletMovimientos.AsNoTracking()
            .Where(m => m.IdWallet == s.IdWallet).ToListAsync();
        var mov = Assert.Single(wmov);
        Assert.Equal(tipoMov, mov.TipoMovimiento);
        Assert.Equal("C", mov.Naturaleza);
        Assert.Equal(Valor, mov.Valor);
    }

    private static async Task AssertSinMovimientoAsync(string cs, Sembrado s, string estadoEsperado)
    {
        await using var v = NuevoContexto(cs);
        var retiro = await v.PassportBrebRetiros.AsNoTracking().SingleAsync(x => x.IdBrebRetiro == s.IdRetiro);
        Assert.Equal(estadoEsperado, retiro.Estado);
        Assert.Null(retiro.ResolucionInciertaTipo);
        var saldo = await v.WalletSaldos.AsNoTracking().SingleAsync(x => x.IdWallet == s.IdWallet);
        Assert.Equal(DisponibleInicial, saldo.SaldoDisponible);
        Assert.Equal(Valor, saldo.SaldoRetenido);
        Assert.Equal(0, await v.LedgerTransacciones.AsNoTracking()
            .CountAsync(t => t.ReferenciaTipo == "passport_breb_retiros" && t.ReferenciaId == s.IdRetiro));
        Assert.Equal(0, await v.WalletMovimientos.AsNoTracking().CountAsync(m => m.IdWallet == s.IdWallet));
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de J.3/J.6.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var w in creados.Wallets)
            {
                var retiros = await ctx.PassportBrebRetiros.AsNoTracking()
                    .Where(r => r.IdWallet == w).Select(r => r.IdBrebRetiro).ToListAsync();
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_movimientos WHERE id_wallet = {w}");
                foreach (var r in retiros)
                {
                    var txs = await ctx.LedgerTransacciones.AsNoTracking()
                        .Where(t => t.ReferenciaTipo == "passport_breb_retiros" && t.ReferenciaId == r)
                        .Select(t => t.IdTransaccionLedger).ToListAsync();
                    await ctx.Database.ExecuteSqlAsync($"UPDATE dbo.passport_breb_retiros SET id_transaccion_ledger = NULL WHERE id_breb_retiro = {r}");
                    foreach (var t in txs)
                    {
                        await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.ledger_movimientos WHERE id_transaccion_ledger = {t}");
                        await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.ledger_transacciones WHERE id_transaccion_ledger = {t}");
                    }
                }
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.passport_breb_retiros WHERE id_wallet = {w}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.passport_breb_llaves WHERE id_wallet = {w}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {w}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {w}");
            }
            foreach (var u in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {u}");
            foreach (var p in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {p}");
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
