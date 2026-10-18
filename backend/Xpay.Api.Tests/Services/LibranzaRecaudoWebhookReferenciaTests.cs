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

// Correlación del webhook entrante con el lote de recaudo cuando Passport
// devuelve qr_code_reference = "CO.COM.VISI.TRXIDP" + referencia XPAY
// (evidencia M4-T4). Entrada real: parser + PassportWebhookService +
// LibranzaRecaudoAplicacionService. El lote llega a PENDIENTE_PAGO con un
// cliente QR falso (cero HTTP). Mismo patrón SQL que el resto del repositorio:
// se salta en local sin cadena de conexión y es obligatoria en CI.
[Collection("SqlIntegration")]
public sealed class LibranzaRecaudoWebhookReferenciaTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Prefijo = LibranzaRecaudoReferencia.PrefijoPassportQr;
    private const string CuentaOperativa = "acct-test-recaudo";

    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);

    // 1 — settled con prefijo + referencia del lote: se correlaciona y aplica.
    [Fact]
    public async Task SettledConPrefijoYReferenciaCorrecta_AplicaElLote()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarLoteAsync(cs, creados);
            var pid = NuevoPayment(creados);

            var r = await ProcesarAsync(cs, RawSettled(pid, Prefijo + s.Referencia));

            Assert.Equal(PassportWebhookProcessResult.Processed, r.Result);
            Assert.Equal($"RECAUDO:{LibranzaRecaudoAplicacionService.ResAplicado}", await ResultadoEventoAsync(cs, pid));
            await using var v = NuevoContexto(cs);
            var lote = await v.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == s.IdLote);
            Assert.Equal(LibranzaLoteEstados.Aplicado, lote.Estado);
            Assert.Equal(pid, lote.PassportPaymentId);
            Assert.Equal(1, await ContarAplicacionesAsync(cs, s.IdLote));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // 2 — "LLAVE" (pago a llave, no a QR) nunca correlaciona por referencia.
    [Fact]
    public async Task SettledConLlave_NoCorrelacionaNingunLote()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarLoteAsync(cs, creados);
            var pid = NuevoPayment(creados);

            var r = await ProcesarAsync(cs, RawSettled(pid, "LLAVE"));

            Assert.Equal(PassportWebhookProcessResult.Processed, r.Result);
            Assert.Equal($"RECAUDO:{LibranzaRecaudoAplicacionService.ResSinLote}", await ResultadoEventoAsync(cs, pid));
            await AssertLotePendienteSinPagoAsync(cs, s.IdLote);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // 3 — la referencia de B solo puede aplicar a B: A queda intacto.
    [Fact]
    public async Task SettledConReferenciaDeOtroLote_AplicaSoloAEseLote()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var a = await SembrarLoteAsync(cs, creados);
            var b = await SembrarLoteAsync(cs, creados);
            Assert.NotEqual(a.IdConvenio, b.IdConvenio);
            var pid = NuevoPayment(creados);

            var r = await ProcesarAsync(cs, RawSettled(pid, Prefijo + b.Referencia));

            Assert.Equal(PassportWebhookProcessResult.Processed, r.Result);
            Assert.Equal($"RECAUDO:{LibranzaRecaudoAplicacionService.ResAplicado}", await ResultadoEventoAsync(cs, pid));
            await using var v = NuevoContexto(cs);
            var loteB = await v.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == b.IdLote);
            Assert.Equal(LibranzaLoteEstados.Aplicado, loteB.Estado);
            Assert.Equal(pid, loteB.PassportPaymentId);
            Assert.Equal(1, await ContarAplicacionesAsync(cs, b.IdLote));
            await AssertLotePendienteSinPagoAsync(cs, a.IdLote);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // 4 — payment_id tiene prioridad: el lote vinculado se encuentra por el
    // pago aunque la referencia apunte a otro sitio, y la discrepancia lo deja
    // en revisión sin mover dinero. Preparación EF solo en la SQL local.
    [Fact]
    public async Task PagoVinculadoPorPaymentId_ConReferenciaDistinta_RequiereRevision()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var a = await SembrarLoteAsync(cs, creados);
            var pid = NuevoPayment(creados);
            await MarcarPagoEnProcesoAsync(cs, a.IdLote, pid);
            const string otra = "AAAAAAAAAAAAAAAA";
            Assert.NotEqual(otra, a.Referencia);

            var r = await ProcesarAsync(cs, RawSettled(pid, Prefijo + otra));

            Assert.Equal(PassportWebhookProcessResult.Processed, r.Result);
            Assert.Equal($"RECAUDO:{LibranzaRecaudoAplicacionService.ResRevision}", await ResultadoEventoAsync(cs, pid));
            await using var v = NuevoContexto(cs);
            var lote = await v.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == a.IdLote);
            Assert.Equal(LibranzaLoteEstados.RequiereRevision, lote.Estado);
            Assert.Equal("REFERENCIA_NO_COINCIDE", lote.MotivoRevision);
            Assert.Equal(pid, lote.PassportPaymentId);
            Assert.Equal(0, await ContarAplicacionesAsync(cs, a.IdLote));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // 5 — settled repetido: la capa webhook lo trata como Duplicate y la capa
    // financiera devuelve YA_APLICADO; nunca hay una segunda aplicación.
    [Fact]
    public async Task SettledDuplicado_EsIdempotenteYNoAplicaDosVeces()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var s = await SembrarLoteAsync(cs, creados);
            var pid = NuevoPayment(creados);
            var raw = RawSettled(pid, Prefijo + s.Referencia);

            var primera = await ProcesarAsync(cs, raw);
            Assert.Equal(PassportWebhookProcessResult.Processed, primera.Result);
            Assert.NotNull(primera.IdEvent);
            await using (var v1 = NuevoContexto(cs))
                Assert.Equal(LibranzaLoteEstados.Aplicado,
                    (await v1.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == s.IdLote)).Estado);
            Assert.Equal(1, await ContarAplicacionesAsync(cs, s.IdLote));

            var segunda = await ProcesarAsync(cs, raw);
            Assert.Equal(PassportWebhookProcessResult.Duplicate, segunda.Result);
            Assert.Equal(primera.IdEvent, segunda.IdEvent);
            Assert.Equal(1, await ContarAplicacionesAsync(cs, s.IdLote));

            await using (var ctx = NuevoContexto(cs))
            {
                var recaudo = new LibranzaRecaudoAplicacionService(ctx, Config(), NullLogger<LibranzaRecaudoAplicacionService>.Instance);
                var r = await recaudo.ProcesarEventoEntranteAsync(
                    PassportInboundEventKind.Settled, pid, signatureValid: true, primera.IdEvent!.Value);
                Assert.Equal(LibranzaRecaudoAplicacionService.ResYaAplicado, r.Resultado);
                Assert.Equal(s.IdLote, r.IdLote);
            }

            await using var v = NuevoContexto(cs);
            var lote = await v.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == s.IdLote);
            Assert.Equal(LibranzaLoteEstados.Aplicado, lote.Estado);
            Assert.Equal(1, await ContarAplicacionesAsync(cs, s.IdLote));
            Assert.Equal(1, await v.PassportWebhookEvents.AsNoTracking().CountAsync(e => e.PassportPaymentId == pid));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // --- Utilidades del webhook ---------------------------------------------

    private static string NuevoPayment(Sembrados creados)
    {
        var pid = "qrref-" + Guid.NewGuid().ToString("N")[..20];
        creados.Payments.Add(pid);
        return pid;
    }

    private static string RawSettled(string pid, string referencia) =>
        "{\"Event\":\"" + PassportInboundEventClassifier.EventInboundSettled + "\"," +
        "\"payment_id\":\"" + pid + "\"," +
        "\"status\":\"SETTLED\"," +
        "\"account_id\":\"" + CuentaOperativa + "\"," +
        "\"amount\":{\"value\":\"105950.00\",\"currency\":\"COP\"}," +
        "\"qr_code_reference\":\"" + referencia + "\"," +
        "\"end_to_end_identification\":\"E2E" + pid[6..] + "\"," +
        "\"created_at\":\"2026-10-20T14:59:00Z\"," +
        "\"updated_at\":\"2026-10-20T15:00:00Z\"," +
        "\"settled_at\":\"2026-10-20T15:00:00Z\"}";

    private static async Task<PassportWebhookProcessOutcome> ProcesarAsync(string cs, string raw)
    {
        Assert.True(PassportWebhookPayloadParser.TryParse(raw, out var payload));
        await using var ctx = NuevoContexto(cs);
        var svc = new PassportWebhookService(
            ctx,
            new FijoTimeProvider(T0),
            new LibranzaRecaudoAplicacionService(ctx, Config(), NullLogger<LibranzaRecaudoAplicacionService>.Instance),
            NullLogger<PassportWebhookService>.Instance);
        return await svc.ProcessAsync(raw, payload, signatureValid: true);
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvCollectionKeyId]       = "key-test-recaudo",
            [PassportOptions.EnvOperationalCustomerId] = "customer-test-recaudo",
            [PassportOptions.EnvOperationalAccountId]  = CuentaOperativa,
        })
        .Build();

    private static async Task<string?> ResultadoEventoAsync(string cs, string pid)
    {
        await using var v = NuevoContexto(cs);
        var fila = await v.PassportWebhookEvents.AsNoTracking().SingleAsync(e => e.PassportPaymentId == pid);
        return fila.ErrorMessage;
    }

    private static async Task<int> ContarAplicacionesAsync(string cs, long idLote)
    {
        await using var v = NuevoContexto(cs);
        return await v.LedgerTransacciones.AsNoTracking()
            .CountAsync(t => t.ReferenciaTipo == "libranza_lotes_recaudo" && t.ReferenciaId == idLote);
    }

    private static async Task AssertLotePendienteSinPagoAsync(string cs, long idLote)
    {
        await using var v = NuevoContexto(cs);
        var lote = await v.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == idLote);
        Assert.Equal(LibranzaLoteEstados.PendientePago, lote.Estado);
        Assert.Null(lote.PassportPaymentId);
        Assert.Equal(0, await ContarAplicacionesAsync(cs, idLote));
    }

    // --- Utilidades de siembra ----------------------------------------------

    private sealed record Sembrado(long IdUsuario, long IdConvenio, long IdLote, string Referencia);

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets { get; } = new();
        public List<long> Convenios { get; } = new();
        public List<string> Payments { get; } = new();
    }

    private sealed class FakeQrClient(Func<PassportCreateQrCodeRequest, PassportQrCodeResponse> responder) : IPassportQrClient
    {
        public Task<PassportQrCodeResponse> CreateQrCodeAsync(
            PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(responder(request));

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(PassportDecodeQrCodeResponse? Response, string? RawResponseJson)> DecodeQrCodeWithRawCaptureAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    // Respuesta coherente con la solicitud: referencia con el prefijo Passport,
    // mismo monto (105950.00), COP, id y qr_code_data presentes.
    private static PassportQrCodeResponse Respuesta(PassportCreateQrCodeRequest req) => new()
    {
        Id              = "qr-test-" + req.QrCodeReference,
        Status          = "ACTIVE",
        Type            = "DYNAMIC",
        QrCodeData      = "000201010212TEST",
        QrCodeReference = Prefijo + req.QrCodeReference,
        Amount          = new PassportQrAmountResponse { Value = req.Amount!.Value, Currency = "COP" },
    };

    // Solo caso 4, solo SQL local: simula un lote ya vinculado por payment_id.
    private static async Task MarcarPagoEnProcesoAsync(string cs, long idLote, string pid)
    {
        await using var ctx = NuevoContexto(cs);
        var lote = await ctx.LibranzaLotesRecaudo.SingleAsync(l => l.IdLote == idLote);
        Assert.Equal(LibranzaLoteEstados.PendientePago, lote.Estado);
        Assert.Null(lote.PassportPaymentId);
        lote.Estado            = LibranzaLoteEstados.PagoEnProceso;
        lote.PassportPaymentId = pid;
        Assert.Equal(1, await ctx.SaveChangesAsync());
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
                NumeroDocumento = $"65{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                PrimerNombre = "WhRef", PrimerApellido = "Sintetico", Celular = "3000000000",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"whref_{sufijo}",
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
                NombreEmpresa = $"Conv wh {sufijo}",
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
                Nombres = "WhRef", SalarioMensual = 1_000_000m, PeriodicidadPago = "MENSUAL", Estado = "ACTIVO",
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

        long idLote;
        string referencia;
        await using (var ctx = NuevoContexto(cs))
        {
            var svc = new LibranzaRecaudoService(ctx, new FakeQrClient(_ => throw new InvalidOperationException()),
                Config(), NullLogger<LibranzaRecaudoService>.Instance);
            var lote = await svc.CrearLoteAsync(idConvenio, new DateOnly(2026, 10, 20), idUsuario);
            Assert.Equal(LibranzaLoteEstados.Borrador, lote.Estado);
            Assert.Equal(105_950m, lote.MontoTotal);
            Assert.True(LibranzaRecaudoReferencia.EsValida(lote.ReferenciaPublica));
            idLote = lote.IdLote;
            referencia = lote.ReferenciaPublica;
        }

        await using (var ctx = NuevoContexto(cs))
        {
            var svc = new LibranzaRecaudoService(ctx, new FakeQrClient(req => Respuesta(req)),
                Config(), NullLogger<LibranzaRecaudoService>.Instance);
            var r = await svc.GenerarQrAsync(idLote, idConvenio, idUsuario);
            Assert.Equal(LibranzaLoteEstados.PendientePago, r.Estado);
        }

        await using (var v = NuevoContexto(cs))
        {
            var lote = await v.LibranzaLotesRecaudo.AsNoTracking().SingleAsync(l => l.IdLote == idLote);
            Assert.Equal(LibranzaLoteEstados.PendientePago, lote.Estado);
            Assert.Equal(LibranzaLoteEstados.QrGenerado, lote.QrEstado);
            Assert.False(string.IsNullOrWhiteSpace(lote.PassportQrId));
            Assert.False(string.IsNullOrWhiteSpace(lote.QrCodeData));
            Assert.Equal(referencia, lote.ReferenciaPublica);
        }

        return new Sembrado(idUsuario, idConvenio, idLote, referencia);
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;

        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de webhook de recaudo.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Solo ids exactos registrados en Sembrados (o derivados de ellos); nunca
    // unidades_negocio ni ledger_cuentas. Orden verificado contra las FK.
    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            var convenios = creados.Convenios;
            var pagos     = creados.Payments;

            var lotes = await ctx.LibranzaLotesRecaudo.AsNoTracking()
                .Where(l => convenios.Contains(l.IdConvenio))
                .Select(l => new { l.IdLote, l.IdTransaccionLedgerAplicacion })
                .ToListAsync();
            var idsLote = lotes.Select(l => l.IdLote).ToList();
            var idsTx   = lotes.Where(l => l.IdTransaccionLedgerAplicacion != null)
                .Select(l => l.IdTransaccionLedgerAplicacion!.Value).Distinct().ToList();

            await ctx.PassportWebhookEvents
                .Where(e => e.PassportPaymentId != null && pagos.Contains(e.PassportPaymentId)).ExecuteDeleteAsync();
            await ctx.LedgerMovimientos.Where(m => idsTx.Contains(m.IdTransaccionLedger)).ExecuteDeleteAsync();
            await ctx.LedgerTransacciones.Where(t => idsTx.Contains(t.IdTransaccionLedger)).ExecuteDeleteAsync();

            await ctx.LibranzaLoteRecaudoDetalles.Where(d => idsLote.Contains(d.IdLote)).ExecuteDeleteAsync();
            await ctx.LibranzaLotesRecaudo.Where(l => idsLote.Contains(l.IdLote)).ExecuteDeleteAsync();
            await ctx.LibranzaAnticipos.Where(a => convenios.Contains(a.IdConvenio)).ExecuteDeleteAsync();
            await ctx.LibranzaEmpleados.Where(e => convenios.Contains(e.IdConvenio)).ExecuteDeleteAsync();
            await ctx.LibranzaEmpresasConvenio.Where(c => convenios.Contains(c.IdConvenio)).ExecuteDeleteAsync();

            await ctx.Wallets.Where(w => creados.Wallets.Contains(w.IdWallet)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => creados.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => creados.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
