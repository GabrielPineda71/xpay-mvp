using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Anticipo de nómina — sin límite de CANTIDAD de anticipos (decisión de
// producto): la única restricción acumulativa es el cupo del corte. Mismo
// patrón SQL que CarteraMaterializacionCupoTests: requiere base real, se salta
// en local sin cadena de conexión y es obligatoria en CI.
//
// Escenario: corte único día 20, pago programado 1.000.000, 30 % → cupo
// 300.000 para la fecha simulada 2026-10-17 (fecha de pago 2026-10-20).
// Los parámetros se siembran con PermiteAnticipoMultiple=false y
// MaxAnticipacionesActivos=1 (configuración real de QA) para demostrar que ya
// no limitan.
[Collection("SqlIntegration")]
public sealed class LibranzaAnticipoMultipleTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string FechaSimulada = "2026-10-17";

    // A + B + C + D — varios anticipos del mismo corte hasta agotar el cupo
    // exactamente; el siguiente se rechaza por cupo, no por cantidad.
    [Fact]
    public async Task VariosAnticiposMismoCorte_HastaElCupo_ElSiguienteSeRechazaPorCupo()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await new SeedContext(cs).SembrarEmpleadoAsync(creados);

            // A — primer anticipo dentro del cupo.
            var a = await SolicitarAsync(cs, s, 100_000m);
            Assert.Equal("DESEMBOLSADO", a.Estado);
            Assert.Equal(new DateOnly(2026, 10, 20).ToString("yyyy-MM-dd"), a.FechaPagoProgramada);

            // B — segundo anticipo del mismo corte mientras queda cupo.
            var b = await SolicitarAsync(cs, s, 100_000m);
            Assert.Equal("DESEMBOLSADO", b.Estado);
            Assert.NotEqual(a.IdAnticipo, b.IdAnticipo);

            // C — la suma llega exactamente al cupo (300.000).
            var c = await SolicitarAsync(cs, s, 100_000m);
            Assert.Equal("DESEMBOLSADO", c.Estado);

            // D — el siguiente excede el cupo restante (0) → rechazado por cupo.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SolicitarAsync(cs, s, 100_000m));
            Assert.Contains("supera el cupo disponible", ex.Message);
            Assert.DoesNotContain("máximo permitido", ex.Message);

            await using var v = NuevoContexto(cs);
            var anticipos = await v.LibranzaAnticipos.AsNoTracking()
                .Where(x => x.IdEmpleado == s.IdEmpleado).ToListAsync();
            Assert.Equal(3, anticipos.Count);
            Assert.Equal(300_000m, anticipos.Sum(x => x.ValorSolicitado));
            var saldo = await v.WalletSaldos.AsNoTracking().SingleAsync(x => x.IdWallet == s.IdWallet);
            Assert.Equal(300_000m, saldo.SaldoDisponible);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // E — un anticipo DESEMBOLSADO de un corte histórico distinto no bloquea
    // ni consume cupo del corte actual.
    [Fact]
    public async Task AnticipoDesembolsadoDeOtroCorte_NoBloqueaElCorteActual()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var seed = new SeedContext(cs);
            var s = await seed.SembrarEmpleadoAsync(creados);
            await seed.SembrarAnticipoHistoricoAsync(s, new DateOnly(2026, 7, 20), 100_000m);

            var r = await SolicitarAsync(cs, s, 300_000m);
            Assert.Equal("DESEMBOLSADO", r.Estado);
            Assert.Equal(300_000m, r.ValorSolicitado);

            await using var v = NuevoContexto(cs);
            Assert.Equal(2, await v.LibranzaAnticipos.AsNoTracking().CountAsync(x => x.IdEmpleado == s.IdEmpleado));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // --- Utilidades ---------------------------------------------------------

    private static async Task<AnticipoResponse> SolicitarAsync(string cs, SembradoEmpleado s, decimal valor)
    {
        await using var ctx = NuevoContexto(cs);
        var svc = new LibranzaAnticipoService(ctx, NullLogger<LibranzaAnticipoService>.Instance);
        var req = new SolicitarAnticipoRequest { ValorSolicitado = valor, FechaSimulada = FechaSimulada };
        var r = await svc.SolicitarAnticipoAsync(s.IdUsuario, req, Guid.NewGuid());
        return r.Data;
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;

        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de anticipo múltiple.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private sealed class SembradoEmpleado
    {
        public long IdUsuario { get; init; }
        public long IdWallet { get; init; }
        public long IdEmpleado { get; init; }
        public long IdConvenio { get; init; }
    }

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets { get; } = new();
        public List<long> Empleados { get; } = new();
        public List<long> Convenios { get; } = new();
    }

    private sealed class SeedContext(string cs)
    {
        public async Task<SembradoEmpleado> SembrarEmpleadoAsync(Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var idUnidad = await ctx.Database
                .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
                .SingleAsync();
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..12];

            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad,
                TipoDocumento = "CC",
                NumeroDocumento = $"77{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                PrimerNombre = "MultiAnt",
                PrimerApellido = "Sintetico",
                Celular = "3000000000",
                Pais = "Colombia",
                Estado = "ACTIVA",
                FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona,
                NombreUsuario = $"multiant_{sufijo}",
                PasswordHash = "x",
                Estado = "ACTIVO",
                FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            creados.Usuarios.Add(usuario.IdUsuario);

            var wallet = new Wallet
            {
                IdUnidadNegocio = idUnidad,
                TipoWallet = "PERSONA",
                IdPersona = persona.IdPersona,
                NombreWallet = $"w_{sufijo}",
                Estado = "ACTIVA",
                FechaCreacion = ahora,
            };
            ctx.Wallets.Add(wallet);
            await ctx.SaveChangesAsync();
            creados.Wallets.Add(wallet.IdWallet);
            ctx.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = ahora });

            var convenio = new LibranzaEmpresaConvenio
            {
                NombreEmpresa = $"Conv multi {sufijo}",
                Nit = $"9{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}",
                Estado = "ACTIVO",
                PeriodicidadPago = "MENSUAL",
                PorcentajeMaximoCupo = 30m,
                FechaInicio = ahora,
                CreatedAt = ahora,
            };
            ctx.LibranzaEmpresasConvenio.Add(convenio);
            await ctx.SaveChangesAsync();
            creados.Convenios.Add(convenio.IdConvenio);

            ctx.LibranzaParametrosEmpresas.Add(new LibranzaParametrosEmpresa
            {
                IdConvenio = convenio.IdConvenio,
                PorcentajeMaximoCupo = 30m,
                PermiteAnticipoMultiple = false,
                MaxAnticipacionesActivos = 1,
                MomentoCobroComision = "VENCIDO",
                MontoMinimoAnticipo = 100_000m,
                Estado = "ACTIVO",
                CreatedAt = ahora,
            });
            ctx.LibranzaRangosCobro.Add(new LibranzaRangoCobro
            {
                IdConvenio = convenio.IdConvenio,
                ValorDesde = 1m,
                ValorHasta = 10_000_000m,
                TipoCobro = "FIJO",
                ValorCobro = 5_000m,
                AplicaIva = true,
                Estado = "ACTIVO",
                CreatedAt = ahora,
            });

            var empleado = new LibranzaEmpleado
            {
                IdConvenio = convenio.IdConvenio,
                TipoDocumento = "CC",
                NumeroDocumento = persona.NumeroDocumento,
                Nombres = "MultiAnt",
                SalarioMensual = 1_000_000m,
                PeriodicidadPago = "MENSUAL",
                Estado = "ACTIVO",
                CupoPreliminar = 300_000m,
                OrigenCarga = "MANUAL",
                CreatedAt = ahora,
            };
            ctx.LibranzaEmpleados.Add(empleado);
            await ctx.SaveChangesAsync();
            creados.Empleados.Add(empleado.IdEmpleado);

            ctx.LibranzaEmpleadoCortesPago.Add(new LibranzaEmpleadoCortesPago
            {
                IdEmpleado = empleado.IdEmpleado,
                NumeroCorte = 1,
                DiaPago = 20,
                ValorPagoProgramado = 1_000_000m,
                Estado = "ACTIVO",
                CreatedAt = ahora,
            });
            ctx.LibranzaEmpleadoUsuarios.Add(new LibranzaEmpleadoUsuario
            {
                IdEmpleado = empleado.IdEmpleado,
                IdUsuario = usuario.IdUsuario,
                IdWallet = wallet.IdWallet,
                Estado = "ACTIVO",
                CreatedAt = ahora,
            });
            await ctx.SaveChangesAsync();

            return new SembradoEmpleado
            {
                IdUsuario = usuario.IdUsuario,
                IdWallet = wallet.IdWallet,
                IdEmpleado = empleado.IdEmpleado,
                IdConvenio = convenio.IdConvenio,
            };
        }

        public async Task SembrarAnticipoHistoricoAsync(SembradoEmpleado s, DateOnly fechaPago, decimal valor)
        {
            await using var ctx = NuevoContexto(cs);
            ctx.LibranzaAnticipos.Add(new LibranzaAnticipo
            {
                IdConvenio = s.IdConvenio,
                IdEmpleado = s.IdEmpleado,
                IdUsuario = s.IdUsuario,
                IdWallet = s.IdWallet,
                FechaSolicitud = DateTime.UtcNow.AddMonths(-3),
                DiaPagoCorte = fechaPago.Day,
                FechaPagoProgramada = fechaPago,
                ValorPagoProgramado = 1_000_000m,
                PorcentajeCupo = 30m,
                ValorCupoBase = 300_000m,
                ValorSolicitado = valor,
                ValorComision = 5_000m,
                ValorIva = 950m,
                ValorTotalACobrar = valor + 5_950m,
                ValorNetoDesembolsado = valor,
                MomentoCobroComision = "VENCIDO",
                Estado = "DESEMBOLSADO",
                CreatedAt = DateTime.UtcNow.AddMonths(-3),
            });
            await ctx.SaveChangesAsync();
        }
    }

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_idempotencia WHERE id_usuario = {id}");
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_movimientos WHERE id_wallet = {id}");
            foreach (var id in creados.Empleados)
            {
                var txs = await ctx.Database.SqlQuery<long>($@"SELECT id_transaccion_ledger AS Value FROM dbo.ledger_transacciones
                    WHERE referencia_tipo = 'libranza_anticipos'
                      AND referencia_id IN (SELECT id_anticipo FROM dbo.libranza_anticipos WHERE id_empleado = {id})").ToListAsync();
                foreach (var tx in txs)
                    await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.ledger_movimientos WHERE id_transaccion_ledger = {tx}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_anticipos WHERE id_empleado = {id}");
                foreach (var tx in txs)
                    await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.ledger_transacciones WHERE id_transaccion_ledger = {tx}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empleado_usuario WHERE id_empleado = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empleado_cortes_pago WHERE id_empleado = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empleados WHERE id_empleado = {id}");
            }
            foreach (var id in creados.Convenios)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_rangos_cobro WHERE id_convenio = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_parametros_empresa WHERE id_convenio = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empresas_convenio WHERE id_convenio = {id}");
            }
            foreach (var id in creados.Wallets)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
            }
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
