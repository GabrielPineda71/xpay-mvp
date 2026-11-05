using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-647 — costura servicio ↔ cliente REAL. Los tests de
// CarteraConsultaRiesgoServiceTests usan FakeMiDecisorClient y los de
// MiDecisorClientTests construyen el request a mano, así que ninguno ejercía
// PersonaMiDecisorRequestMapper → MiDecisorClient juntos: la doble traducción
// de tipoIdentificacion ("CC" → "1" → rechazo) pasó inadvertida.
//
// Aquí: CarteraConsultaRiesgoService real + MiDecisorClient real +
// HttpMessageHandler falso en memoria. SIN red, SIN credenciales, SIN cédulas:
// documento y apellido son sintéticos y ficticios.
public class CarteraConsultaRiesgoMiDecisorSeamTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const long IdSolicitud = 10;
    private const long IdUsuario   = 7;

    private static Persona PersonaFicticia(string tipoDocumento) => new()
    {
        IdPersona       = 3,
        TipoDocumento   = tipoDocumento,
        NumeroDocumento = "1234567",
        PrimerApellido  = "Ficticio",
    };

    private static string AcceptedWithRisk() =>
        """
        {"status":"ACCEPTED","content":{"status":"202 ACCEPTED","respuesta":{"informacionRiesgo":
        {"conInformacion":true,"score":"853","viabilidad":"ALTA","ratingRecaudos":"A",
        "montoSugerido":"13809492","alertas":[]}}}}
        """;

    private static (CarteraConsultaRiesgoService Svc, FakeCarteraConsultaRiesgoStore Store,
        FakeHttpMessageHandler Handler, FakeMiDecisorTokenProvider Token) Crear(
            string tipoDocumento, HttpStatusCode httpStatus = HttpStatusCode.OK)
    {
        var store = new FakeCarteraConsultaRiesgoStore
        {
            Contexto = new ConsultaRiesgoContexto(
                IdSolicitud, IdUsuario, 3, CarteraSolicitudCupoEstados.Recibida, PersonaFicticia(tipoDocumento)),
        };
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(httpStatus, AcceptedWithRisk()));
        var token = new FakeMiDecisorTokenProvider();
        var client = new MiDecisorClient(
            new FakeHttpClientFactory(handler),
            token,
            new FakeConfiguration(new Dictionary<string, string?>
            {
                [MiDecisorOptions.EnvBaseUrl] = "https://midecisor.test",
            }),
            NullLogger<MiDecisorClient>.Instance);

        var svc = new CarteraConsultaRiesgoService(
            store,
            client,
            new FakeConsultaRiesgoAutorizacion(true),
            new FijoTimeProvider(T0),
            new CapturingLogger());

        return (svc, store, handler, token);
    }

    // XPAY-652: el mismo flujo con HTTP 200 y con HTTP 202 real; el status
    // persistido es el observado por el cliente, no content.status.
    [Theory]
    [InlineData(HttpStatusCode.OK, 200)]
    [InlineData(HttpStatusCode.Accepted, 202)]
    public async Task PersonaCC_ThroughRealClient_SendsTipoIdentificacion1(HttpStatusCode httpStatus, int esperado)
    {
        var (svc, store, handler, token) = Crear("CC", httpStatus);

        var r = await svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr");

        Assert.Equal(1, token.CallCount);
        Assert.Equal(1, handler.CallCount);
        var body = handler.LastRequestBody!;
        Assert.Contains("\"tipoIdentificacion\":\"1\"", body);
        Assert.Contains("\"numeroIdentificacion\":\"1234567\"", body);
        Assert.Contains("\"apellidoRazonSocial\":\"Ficticio\"", body);

        Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, r.EstadoSolicitud);
        Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, r.ResultadoTecnico);
        Assert.True(store.FinalizoConExito);

        var o = store.UltimoOutcome!;
        Assert.Equal(CarteraSolicitudCupoEstados.EnEvaluacion, o.EstadoSolicitudFinal);
        Assert.Equal(CarteraConsultaRiesgoResultados.Aceptada, o.ResultadoTecnico);
        Assert.Equal(esperado, o.HttpStatusObservado);
        Assert.Equal("202 ACCEPTED", o.ContentStatusObservado);
    }

    // Un tipo XPAY no soportado (Persona Jurídica / desconocido) sigue fallando
    // cerrado en el pre-flight: sin transición durable, sin token, sin HTTP.
    [Theory]
    [InlineData("NIT")]
    [InlineData("XX")]
    [InlineData("1")]
    public async Task PersonaTipoNoSoportado_FailsClosed_NoTokenNoHttp(string tipoDocumento)
    {
        var (svc, store, handler, token) = Crear(tipoDocumento);

        await Assert.ThrowsAsync<MiDecisorRequestValidationException>(
            () => svc.EjecutarConsultaRiesgoAsync(IdSolicitud, IdUsuario, "corr"));

        Assert.Equal(0, store.IniciarCalls);
        Assert.Equal(0, store.MarcarCalls);
        Assert.Equal(0, token.CallCount);
        Assert.Equal(0, handler.CallCount);
    }
}
