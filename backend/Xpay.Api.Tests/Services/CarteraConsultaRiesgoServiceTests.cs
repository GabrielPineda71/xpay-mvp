using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// M2.3a/b1 — tests del orquestador de consulta de riesgo. SIN red, SIN proveedor
// real, SIN credenciales, SIN cédulas. Documentos sintéticos ("1234567") que
// no derivan de ninguna lista autorizada.
public class CarteraConsultaRiesgoServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const long IdSolicitud = 10;
    private const long IdUsuario   = 7;

    private static Persona PersonaValida() => new()
    {
        IdPersona       = 3,
        TipoDocumento   = "CC",
        NumeroDocumento = "1234567",
        PrimerApellido  = "Rodriguez",
    };

    private static ConsultaRiesgoContexto Ctx(
        string estado = "RECIBIDA", Persona? persona = null, long idUsuario = IdUsuario)
        => new(IdSolicitud, idUsuario, 3, estado, persona ?? PersonaValida());

    private static MiDecisorResultado ResAceptada(bool? conInformacion = true, string score = "853")
        => new("ACCEPTED", "202 ACCEPTED", conInformacion, score, "ALTA", "A", "13809492", 2);

    private static CarteraConsultaRiesgoService CrearSvc(
        FakeCarteraConsultaRiesgoStore store,
        bool autoriza,
        FakeMiDecisorClient client,
        CapturingLogger? logger = null)
        => new(
            store,
            client,
            new FakeConsultaRiesgoAutorizacion(autoriza),
            new FijoTimeProvider(T0),
            logger ?? new CapturingLogger());

    // ════════════════════ M2.3a — pre-flight / TX-A / orden ════════════════════

    // 1 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConsentFalse_NoProviderCall_StaysRecibida()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, autoriza: false, client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));

        Assert.Equal(0, client.CallCount);
        Assert.Equal(0, store.IniciarCalls);
        Assert.Equal(0, store.MarcarCalls);
        Assert.Equal(0, store.FinalizarCalls);
    }

    // 2 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task MissingSolicitud_ThrowsKeyNotFound()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = null };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));
        Assert.Equal(0, client.CallCount);
    }

    // 3 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task OwnershipMismatch_ThrowsKeyNotFound()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx(idUsuario: 999) };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));
        Assert.Equal(0, client.CallCount);
    }

    // 4 ─────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("CONSULTANDO_RIESGO")]
    [InlineData("EN_EVALUACION")]
    [InlineData("ERROR_PROVEEDOR")]
    public async Task WrongStartingState_ThrowsInvalidOperation_NoProviderCall(string estado)
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx(estado) };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));
        Assert.Equal(0, client.CallCount);
        Assert.Equal(0, store.IniciarCalls);
    }

    // 5 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task MissingPersona_ThrowsKeyNotFound()
    {
        var store = new FakeCarteraConsultaRiesgoStore
        {
            Contexto = new ConsultaRiesgoContexto(IdSolicitud, IdUsuario, 3, "RECIBIDA", Persona: null),
        };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));
        Assert.Equal(0, client.CallCount);
    }

    // 6 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task MissingIdentityField_ThrowsRequestValidation_NoProviderCall()
    {
        var persona = PersonaValida();
        persona.NumeroDocumento = null;
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx(persona: persona) };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<MiDecisorRequestValidationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));
        Assert.Equal(0, client.CallCount);
        Assert.Equal(0, store.IniciarCalls);
    }

    // 7 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task UnsupportedDocType_ThrowsRequestValidation_NoProviderCall()
    {
        var persona = PersonaValida();
        persona.TipoDocumento = "NIT";
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx(persona: persona) };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<MiDecisorRequestValidationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));
        Assert.Equal(0, client.CallCount);
    }

    // 8 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AcceptedConInformacionTrue_MapsAceptada_ExactlyOneCall()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(ResAceptada(conInformacion: true));
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, r.EstadoSolicitud);
        Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, r.ResultadoTecnico);
        Assert.True(r.EsResultadoUtil);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, store.IniciarCalls);
        Assert.Equal(1, store.MarcarCalls);
        Assert.Equal(1, store.FinalizarCalls);

        var o = store.UltimoOutcome!;
        Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, o.EstadoSolicitudFinal);
        Assert.Equal(200, o.HttpStatusObservado);
        Assert.Equal("202 ACCEPTED", o.ContentStatusObservado);
        Assert.True(o.EsResultadoUtil);
        Assert.Equal(T0.UtcDateTime, o.FechaFinUtc);
        // request enviado: tipo mapeado CC → "1"
        Assert.Equal("1", client.LastRequest!.TipoIdentificacion);
        Assert.Equal("1234567", client.LastRequest.NumeroIdentificacion);
        Assert.Equal("Rodriguez", client.LastRequest.ApellidoRazonSocial);
    }

    // 9 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AcceptedConInformacionFalse_MapsSinInformacion()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(ResAceptada(conInformacion: false, score: "-"));
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraConsultaRiesgoResultados.SinInformacion, r.ResultadoTecnico);
        Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, r.EstadoSolicitud);
        Assert.True(r.EsResultadoUtil);
        Assert.Equal(1, client.CallCount);
    }

    // 10 ── score "-" NO determina SIN_INFORMACION si ConInformacion==true ──
    [Fact]
    public async Task ConInformacionTrue_ScoreGuion_StillAceptada()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, "-", null, null, "-", 0));
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, r.ResultadoTecnico);
    }

    // 11–16 ── excepciones de dominio → resultado_tecnico ───────────────────
    public static IEnumerable<object[]> FallasProveedor() => new[]
    {
        new object[] { new MiDecisorQueryRejectedException("x"),     CarteraConsultaRiesgoResultados.RechazadaProveedor },
        new object[] { new MiDecisorAuthenticationException("x"),    CarteraConsultaRiesgoResultados.ErrorAutenticacion },
        new object[] { new MiDecisorConfigurationException("x"),     CarteraConsultaRiesgoResultados.ErrorConfiguracion },
        new object[] { new MiDecisorProtocolException("x"),          CarteraConsultaRiesgoResultados.ErrorProtocolo },
        new object[] { new MiDecisorTransportException("x"),         CarteraConsultaRiesgoResultados.ResultadoIncierto },
        new object[] { new MiDecisorRequestValidationException("x"), CarteraConsultaRiesgoResultados.ErrorValidacionLocal },
    };

    [Theory]
    [MemberData(nameof(FallasProveedor))]
    public async Task MiDecisorException_MapsToOutcome_ErrorProveedor_NoRetry(
        MiDecisorException ex, string resultadoTecnicoEsperado)
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(toThrow: ex);
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(resultadoTecnicoEsperado, r.ResultadoTecnico);
        Assert.Equal(CarteraSolicitudCupoEstados.ErrorProveedor, r.EstadoSolicitud);
        Assert.False(r.EsResultadoUtil);
        Assert.Equal(1, client.CallCount);          // sin retry
        Assert.Equal(1, store.FinalizarCalls);
        Assert.Null(store.UltimoOutcome!.HttpStatusObservado);
        Assert.Null(store.UltimoOutcome.ContentStatusObservado);
        Assert.Equal(T0.UtcDateTime, store.UltimoOutcome.FechaFinUtc);
        // b1: ningún crudo se persiste cuando no llegó un MiDecisorResultado.
        AssertCrudosNulos(store.UltimoOutcome);
    }

    // 17 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CancellationBeforeTxA_Propagates_NoProviderCall()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr", cts.Token));

        Assert.Equal(0, store.CargarCalls);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(0, store.FinalizarCalls);
    }

    // 18 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CancellationDuringProvider_PersistsResultadoIncierto_ThenRethrows()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        using var cts = new CancellationTokenSource();
        var client = new FakeMiDecisorClient(
            ResAceptada(),
            beforeReturn: _ => { cts.Cancel(); return Task.CompletedTask; });
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr", cts.Token));

        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, store.FinalizarCalls);
        Assert.Equal(CarteraConsultaRiesgoResultados.ResultadoIncierto, store.UltimoOutcome!.ResultadoTecnico);
        Assert.Equal(CarteraSolicitudCupoEstados.ErrorProveedor, store.UltimoOutcome.EstadoSolicitudFinal);
        Assert.False(store.UltimoOutcome.EsResultadoUtil);
        Assert.False(store.FinalizarRecibioTokenCancelable);   // TX-B usó CancellationToken.None
        AssertCrudosNulos(store.UltimoOutcome);
    }

    // 19 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SequentialDuplicate_LosesTransition_NoProviderCall()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx(), GanaTransicion = false };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));

        Assert.Equal(1, store.IniciarCalls);
        Assert.Equal(0, store.MarcarCalls);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(0, store.FinalizarCalls);
    }

    // 20 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task StoreFailureBeforeProvider_Propagates_NoProviderCall()
    {
        var store = new FakeCarteraConsultaRiesgoStore
        {
            Contexto = Ctx(),
            IniciarThrows = new InvalidOperationException("db down en TX-A"),
        };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));
        Assert.Equal(0, client.CallCount);
        Assert.Equal(0, store.FinalizarCalls);
    }

    // 21 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task StoreFailureAfterProvider_Propagates_NoRetry()
    {
        var store = new FakeCarteraConsultaRiesgoStore
        {
            Contexto = Ctx(),
            FinalizarThrows = new InvalidOperationException("db down en TX-B"),
        };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));
        Assert.Equal(1, client.CallCount);      // exactamente una, sin retry
        Assert.Equal(1, store.FinalizarCalls);
        Assert.False(store.FinalizoConExito);
    }

    // 24 ── nunca produce una decisión de crédito ──────────────────────────
    [Fact]
    public async Task NeverProducesCreditDecisionState()
    {
        var terminales = new[]
        {
            CarteraSolicitudCupoEstados.EnEvaluacion,
            CarteraSolicitudCupoEstados.ErrorProveedor,
        };

        foreach (var res in new[] { ResAceptada(true), ResAceptada(false) })
        {
            var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
            var client = new FakeMiDecisorClient(res);
            var svc = CrearSvc(store, true, client);
            var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");
            Assert.Contains(r.EstadoSolicitud, terminales);
            Assert.NotEqual("APROBADA", r.EstadoSolicitud);
            Assert.NotEqual("RECHAZADA", r.EstadoSolicitud);
        }
    }

    // 27 ── sin PII / datos de riesgo en logs ──────────────────────────────
    [Fact]
    public async Task NoPiiLogging()
    {
        var logger = new CapturingLogger();
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(ResAceptada(score: "853"));
        var svc = CrearSvc(store, true, client, logger);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        var all = string.Join("\n", logger.Mensajes);
        Assert.DoesNotContain("Rodriguez", all);
        Assert.DoesNotContain("1234567", all);
        Assert.DoesNotContain("853", all);
        Assert.DoesNotContain("13809492", all);
        Assert.DoesNotContain("ALTA", all);
    }

    // 28 ── implementación runtime del consentimiento: siempre false ───────
    [Fact]
    public async Task RuntimeConsentImplementation_AlwaysFalse()
    {
        var impl = new AutorizacionConsultaRiesgoNoDisponible();
        Assert.False(await impl.TieneAutorizacionVigenteAsync(1, 1));
        Assert.False(await impl.TieneAutorizacionVigenteAsync(999999, 424242));
    }

    // ════════════════════ M2.3b1 — fase ENVIO_INCIERTO + TX-B ════════════════

    // b1.1 ── la fase ENVIO_INCIERTO se marca (y confirma) ANTES de SendAsync ─
    [Fact]
    public async Task EnvioIncierto_CommittedBeforeProviderCall()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        string? faseAlLlamar = null;
        var client = new FakeMiDecisorClient(
            ResAceptada(),
            beforeReturn: _ => { faseAlLlamar = store.FaseIntento; return Task.CompletedTask; });
        var svc = CrearSvc(store, true, client);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraIntentoFases.EnvioIncierto, faseAlLlamar);
        Assert.Equal(1, store.MarcarCalls);
        // orden: Iniciar → Marcar → (HTTP) → Finalizar
        Assert.Equal(CarteraIntentoFases.EnvioIncierto, store.FaseAlFinalizar);
        Assert.Equal(CarteraIntentoFases.Finalizado, store.FaseIntento);
    }

    // b1.2 ── si MarcarEnvioInciertoAsync falla: 0 llamadas al proveedor ─────
    [Fact]
    public async Task MarcarEnvioInciertoFailure_NoProviderCall_NoFinalize()
    {
        var store = new FakeCarteraConsultaRiesgoStore
        {
            Contexto = Ctx(),
            MarcarThrows = new InvalidOperationException("db down antes de SendAsync"),
        };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));

        Assert.Equal(1, store.IniciarCalls);
        Assert.Equal(1, store.MarcarCalls);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(0, store.FinalizarCalls);
        Assert.Equal(CarteraIntentoFases.PreCall, store.FaseIntento);
    }

    // b1.3 ── excepción inesperada tras ENVIO_INCIERTO: sin 2ª llamada ──────
    [Fact]
    public async Task UnexpectedExceptionAfterEnvioIncierto_NoSecondCall_NoFinalize()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(toThrow: new InvalidOperationException("boom no-dominio"));
        var svc = CrearSvc(store, true, client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));

        Assert.Equal(1, client.CallCount);       // sin retry
        Assert.Equal(0, store.FinalizarCalls);   // TX-B no corre → CONSULTANDO_RIESGO/ENVIO_INCIERTO
        Assert.Equal(CarteraIntentoFases.EnvioIncierto, store.FaseIntento);
    }

    // b1.4 ── ACEPTADA: los 6 crudos se persisten VERBATIM ──────────────────
    [Fact]
    public async Task Aceptada_PersistsRawFieldsVerbatim()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, "853", "ALTA", "A", "13809492", 2));
        var svc = CrearSvc(store, true, client);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        var o = store.UltimoOutcome!;
        Assert.True(o.ConInformacion);
        Assert.Equal("853", o.ScoreRaw);
        Assert.Equal("ALTA", o.ViabilidadRaw);
        Assert.Equal("A", o.RatingRecaudosRaw);
        Assert.Equal("13809492", o.MontoSugeridoRaw);
        Assert.Equal(2, o.AlertasCount);
    }

    // b1.5 ── SIN_INFORMACION: ConInformacion=false, crudos tal cual ────────
    [Fact]
    public async Task SinInformacion_PersistsConInformacionFalse_RawAsProvided()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", false, "-", null, null, "0", 0));
        var svc = CrearSvc(store, true, client);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        var o = store.UltimoOutcome!;
        Assert.Equal(CarteraConsultaRiesgoResultados.SinInformacion, o.ResultadoTecnico);
        Assert.False(o.ConInformacion);
        Assert.Equal("-", o.ScoreRaw);
        Assert.Null(o.ViabilidadRaw);
        Assert.Null(o.RatingRecaudosRaw);
        Assert.Equal("0", o.MontoSugeridoRaw);
        Assert.Equal(0, o.AlertasCount);
    }

    // b1.6 ── ConInformacion ausente (null) → SIN_INFORMACION, crudo null ──
    [Fact]
    public async Task ConInformacionNull_MapsSinInformacion_RawNull()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", null, null, null, null, null, 0));
        var svc = CrearSvc(store, true, client);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        var o = store.UltimoOutcome!;
        Assert.Equal(CarteraConsultaRiesgoResultados.SinInformacion, o.ResultadoTecnico);
        Assert.Null(o.ConInformacion);
        Assert.Null(o.ScoreRaw);
        Assert.Equal(0, o.AlertasCount);
    }

    // b1.7 ── score "" (vacío) se persiste literal, no se convierte a null ─
    [Fact]
    public async Task ScoreRawEmptyString_PersistedLiteral()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, "", "MEDIA", "B", "", 1));
        var svc = CrearSvc(store, true, client);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        var o = store.UltimoOutcome!;
        Assert.Equal("", o.ScoreRaw);
        Assert.Equal("", o.MontoSugeridoRaw);
    }

    // b1.8–13 ── cualquier MiDecisorException → los 6 crudos NULL ───────────
    [Theory]
    [MemberData(nameof(FallasProveedor))]
    public async Task ProviderException_AllRawFieldsNull(MiDecisorException ex, string _)
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(toThrow: ex);
        var svc = CrearSvc(store, true, client);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        AssertCrudosNulos(store.UltimoOutcome!);
        Assert.Equal(CarteraIntentoFases.EnvioIncierto, store.FaseAlFinalizar);
    }

    // b1.14 ── desbordamiento de ScoreRaw → ERROR_PROTOCOLO, 0 crudos ──────
    [Fact]
    public async Task ScoreRawOverflow_ClassifiesErrorProtocolo_NoRawPersisted()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, new string('9', 21), "ALTA", "A", "100", 0));
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraConsultaRiesgoResultados.ErrorProtocolo, r.ResultadoTecnico);
        Assert.Equal(CarteraSolicitudCupoEstados.ErrorProveedor, r.EstadoSolicitud);
        Assert.False(r.EsResultadoUtil);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, store.FinalizarCalls);
        Assert.Equal(CarteraIntentoFases.Finalizado, store.FaseIntento);
        AssertCrudosNulos(store.UltimoOutcome!);
    }

    // b1.15 ── desbordamiento de MontoSugeridoRaw → ERROR_PROTOCOLO ────────
    [Fact]
    public async Task MontoSugeridoRawOverflow_ClassifiesErrorProtocolo()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, "800", "ALTA", "A", new string('1', 21), 0));
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraConsultaRiesgoResultados.ErrorProtocolo, r.ResultadoTecnico);
        AssertCrudosNulos(store.UltimoOutcome!);
    }

    // b1.16 ── desbordamiento de Viabilidad → ERROR_PROTOCOLO ─────────────
    [Fact]
    public async Task ViabilidadOverflow_ClassifiesErrorProtocolo()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, "800", new string('A', 11), "A", "100", 0));
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraConsultaRiesgoResultados.ErrorProtocolo, r.ResultadoTecnico);
        AssertCrudosNulos(store.UltimoOutcome!);
    }

    // b1.17 ── desbordamiento de RatingRecaudos → ERROR_PROTOCOLO ─────────
    [Fact]
    public async Task RatingRecaudosOverflow_ClassifiesErrorProtocolo()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, "800", "ALTA", "ABC", "100", 0));
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraConsultaRiesgoResultados.ErrorProtocolo, r.ResultadoTecnico);
        AssertCrudosNulos(store.UltimoOutcome!);
    }

    // b1.18 ── valores en el límite exacto de longitud → se persisten ─────
    [Fact]
    public async Task RawFieldsAtExactMaxLength_ArePersisted()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var score20 = new string('9', 20);
        var monto20 = new string('1', 20);
        var via10   = new string('A', 10);
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, score20, via10, "AB", monto20, 3));
        var svc = CrearSvc(store, true, client);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, r.ResultadoTecnico);
        var o = store.UltimoOutcome!;
        Assert.Equal(score20, o.ScoreRaw);
        Assert.Equal(monto20, o.MontoSugeridoRaw);
        Assert.Equal(via10, o.ViabilidadRaw);
        Assert.Equal("AB", o.RatingRecaudosRaw);
    }

    // b1.19 ── TX-B recibe el idUsuario del caller (guard de ownership) ────
    [Fact]
    public async Task FinalizarIntento_PassesOutcomeWithFinalState()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        var o = store.UltimoOutcome!;
        Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, o.EstadoSolicitudFinal);
        Assert.Equal(T0.UtcDateTime, o.FechaFinUtc);
    }

    // b1.20 ── el orquestador nunca hace retry (una sola llamada, un solo TX-B) ─
    [Fact]
    public async Task NoRetry_SingleCallSingleFinalize_OnSuccess()
    {
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var client = new FakeMiDecisorClient(ResAceptada());
        var svc = CrearSvc(store, true, client);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, store.MarcarCalls);
        Assert.Equal(1, store.FinalizarCalls);
    }

    // b1.21 ── logging seguro también en el camino de desbordamiento ──────
    [Fact]
    public async Task OverflowPath_NoRawValuesInLogs()
    {
        var logger = new CapturingLogger();
        var store = new FakeCarteraConsultaRiesgoStore { Contexto = Ctx() };
        var overlong = new string('7', 25);
        var client = new FakeMiDecisorClient(
            new MiDecisorResultado("ACCEPTED", "202 ACCEPTED", true, overlong, "ALTA", "A", "100", 0));
        var svc = CrearSvc(store, true, client, logger);

        await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        var all = string.Join("\n", logger.Mensajes);
        Assert.DoesNotContain(overlong, all);
    }

    private static void AssertCrudosNulos(ResultadoIntentoDurable o)
    {
        Assert.Null(o.ConInformacion);
        Assert.Null(o.ScoreRaw);
        Assert.Null(o.ViabilidadRaw);
        Assert.Null(o.RatingRecaudosRaw);
        Assert.Null(o.MontoSugeridoRaw);
        Assert.Null(o.AlertasCount);
    }
}

// Reloj fijo para asserts deterministas de fecha_fin.
internal sealed class FijoTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

// Logger que captura los mensajes ya formateados, para verificar ausencia de PII.
internal sealed class CapturingLogger : ILogger<Xpay.Api.Services.CarteraConsultaRiesgoService>
{
    public List<string> Mensajes { get; } = new();

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Mensajes.Add(formatter(state, exception));
}
