using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// GenerarQrAsync frente a la respuesta de Passport, que devuelve
// qr_code_reference = "CO.COM.VISI.TRXIDP" + referencia XPAY (evidencia M4-T4).
// Cliente QR falso: ninguna llamada real a Passport. Mismo patrón SQL que
// LibranzaAnticipoMultipleTests: se salta en local sin cadena de conexión y es
// obligatoria en CI. El anticipo se siembra directo (sin wallet ni ledger).
[Collection("SqlIntegration")]
public sealed class LibranzaRecaudoGenerarQrTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Prefijo = LibranzaRecaudoReferencia.PrefijoPassportQr;

    [Fact]
    public async Task RespuestaConPrefijoYReferenciaCorrecta_QrGeneradoYPendientePago()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarLoteAsync(cs, creados);
            var fake = new FakeQrClient(req => Respuesta(req));

            var r = await GenerarAsync(cs, s, fake);

            Assert.Equal(1, fake.Llamadas);
            Assert.Equal(LibranzaLoteEstados.PendientePago, r.Estado);
            Assert.Equal(LibranzaLoteEstados.QrGenerado, r.QrEstado);
            await using var v = NuevoContexto(cs);
            var lote = await v.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == s.IdLote);
            Assert.Equal(LibranzaLoteEstados.PendientePago, lote.Estado);
            Assert.Equal(LibranzaLoteEstados.QrGenerado, lote.QrEstado);
            Assert.Equal("qr-test-" + s.Referencia, lote.PassportQrId);
            Assert.Equal(s.Referencia, lote.ReferenciaPublica);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("referencia_otro_lote")]
    [InlineData("referencia_ausente")]
    [InlineData("referencia_llave")]
    [InlineData("monto_distinto")]
    [InlineData("moneda_no_cop")]
    [InlineData("id_ausente")]
    [InlineData("qr_code_data_ausente")]
    public async Task RespuestaInvalida_SeRechazaYLiberaLaMarcaQr(string caso)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarLoteAsync(cs, creados);
            var fake = new FakeQrClient(req =>
            {
                var r = Respuesta(req);
                switch (caso)
                {
                    case "referencia_otro_lote": r.QrCodeReference = Prefijo + "AAAAAAAAAAAAAAAA"; break;
                    case "referencia_ausente":   r.QrCodeReference = null; break;
                    case "referencia_llave":     r.QrCodeReference = "LLAVE"; break;
                    case "monto_distinto":       r.Amount!.Value = "1.00"; break;
                    case "moneda_no_cop":        r.Amount!.Currency = "USD"; break;
                    case "id_ausente":           r.Id = null; break;
                    case "qr_code_data_ausente": r.QrCodeData = null; break;
                }
                return r;
            });

            await Assert.ThrowsAsync<PassportProtocolException>(() => GenerarAsync(cs, s, fake));

            Assert.Equal(1, fake.Llamadas);
            await using var v = NuevoContexto(cs);
            var lote = await v.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == s.IdLote);
            Assert.Equal(LibranzaLoteEstados.Borrador, lote.Estado);
            Assert.Null(lote.QrEstado);
            Assert.Null(lote.FechaQrIntento);
            Assert.Null(lote.PassportQrId);
            Assert.Null(lote.QrCodeData);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // --- Utilidades ---------------------------------------------------------

    private sealed record Sembrado(long IdUsuario, long IdConvenio, long IdLote, string Referencia);

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets { get; } = new();
        public List<long> Convenios { get; } = new();
    }

    private sealed class FakeQrClient(Func<PassportCreateQrCodeRequest, PassportQrCodeResponse> responder) : IPassportQrClient
    {
        public int Llamadas { get; private set; }

        public Task<PassportQrCodeResponse> CreateQrCodeAsync(
            PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            Llamadas++;
            return Task.FromResult(responder(request));
        }

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(PassportDecodeQrCodeResponse? Response, string? RawResponseJson)> DecodeQrCodeWithRawCaptureAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    // Respuesta coherente con la solicitud: referencia con el prefijo Passport,
    // mismo monto, COP, id y qr_code_data presentes.
    private static PassportQrCodeResponse Respuesta(PassportCreateQrCodeRequest req) => new()
    {
        Id              = "qr-test-" + req.QrCodeReference,
        Status          = "ACTIVE",
        Type            = "DYNAMIC",
        QrCodeData      = "000201010212TEST",
        QrCodeReference = Prefijo + req.QrCodeReference,
        Amount          = new PassportQrAmountResponse { Value = req.Amount!.Value, Currency = "COP" },
    };

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvCollectionKeyId]       = "key-test-recaudo",
            [PassportOptions.EnvOperationalCustomerId] = "customer-test-recaudo",
        })
        .Build();

    private static async Task<LoteRecaudoResponse> GenerarAsync(string cs, Sembrado s, IPassportQrClient qr)
    {
        await using var ctx = NuevoContexto(cs);
        var svc = new LibranzaRecaudoService(ctx, qr, Config(), NullLogger<LibranzaRecaudoService>.Instance);
        return await svc.GenerarQrAsync(s.IdLote, s.IdConvenio, s.IdUsuario);
    }

    private static async Task<Sembrado> SembrarLoteAsync(string cs, Sembrados creados)
    {
        long idUsuario, idConvenio;
        await using (var ctx = NuevoContexto(cs))
        {
            var idUnidad = await ctx.Database
                .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
                .SingleAsync();
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..12];

            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
                NumeroDocumento = $"66{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                PrimerNombre = "QrRef", PrimerApellido = "Sintetico", Celular = "3000000000",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"qrref_{sufijo}",
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

            var convenio = new LibranzaEmpresaConvenio
            {
                NombreEmpresa = $"Conv qr {sufijo}",
                Nit = $"9{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}",
                Estado = "ACTIVO", PeriodicidadPago = "MENSUAL", PorcentajeMaximoCupo = 30m,
                FechaInicio = ahora, CreatedAt = ahora,
            };
            ctx.LibranzaEmpresasConvenio.Add(convenio);
            await ctx.SaveChangesAsync();
            creados.Convenios.Add(convenio.IdConvenio);

            var empleado = new LibranzaEmpleado
            {
                IdConvenio = convenio.IdConvenio, TipoDocumento = "CC", NumeroDocumento = persona.NumeroDocumento,
                Nombres = "QrRef", SalarioMensual = 1_000_000m, PeriodicidadPago = "MENSUAL", Estado = "ACTIVO",
                CupoPreliminar = 300_000m, OrigenCarga = "MANUAL", CreatedAt = ahora,
            };
            ctx.LibranzaEmpleados.Add(empleado);
            await ctx.SaveChangesAsync();

            ctx.LibranzaAnticipos.Add(new LibranzaAnticipo
            {
                IdConvenio = convenio.IdConvenio, IdEmpleado = empleado.IdEmpleado,
                IdUsuario = usuario.IdUsuario, IdWallet = wallet.IdWallet,
                FechaSolicitud = ahora, DiaPagoCorte = 20, FechaPagoProgramada = new DateOnly(2026, 10, 20),
                ValorPagoProgramado = 1_000_000m, PorcentajeCupo = 30m, ValorCupoBase = 300_000m,
                ValorSolicitado = 100_000m, ValorComision = 5_000m, ValorIva = 950m,
                ValorTotalACobrar = 105_950m, ValorNetoDesembolsado = 100_000m,
                MomentoCobroComision = "VENCIDO", Estado = "DESEMBOLSADO", CreatedAt = ahora,
            });
            await ctx.SaveChangesAsync();
            idUsuario = usuario.IdUsuario;
            idConvenio = convenio.IdConvenio;
        }

        await using (var ctx = NuevoContexto(cs))
        {
            var svc = new LibranzaRecaudoService(ctx, new FakeQrClient(_ => throw new InvalidOperationException()),
                Config(), NullLogger<LibranzaRecaudoService>.Instance);
            var lote = await svc.CrearLoteAsync(idConvenio, new DateOnly(2026, 10, 20), idUsuario);
            Assert.Equal(LibranzaLoteEstados.Borrador, lote.Estado);
            Assert.Equal(105_950m, lote.MontoTotal);
            return new Sembrado(idUsuario, idConvenio, lote.IdLote, lote.ReferenciaPublica);
        }
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;

        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de generación de QR.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.Convenios)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_lote_recaudo_detalles WHERE id_lote IN (SELECT id_lote FROM dbo.libranza_lotes_recaudo WHERE id_convenio = {id})");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_lotes_recaudo WHERE id_convenio = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_anticipos WHERE id_convenio = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empleados WHERE id_convenio = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.libranza_empresas_convenio WHERE id_convenio = {id}");
            }
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
