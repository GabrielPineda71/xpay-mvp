using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class SincronizacionDocumentoVeriffTests
{
    private static readonly DateTime DecisionUtc = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static Persona Persona() => new() { IdPersona = 5, NombreVerificadoCompleto = "ANA PRUEBA",
        ApellidoVerificadoCompleto = "DOS APELLIDOS", TipoDocumentoVeriffRaw = "ID_CARD",
        NumeroDocumentoVerificado = "1.234.567.890", FechaNacimiento = new(1990, 1, 1) };
    private static KycVerificacion Sesion() => new() { IdPersona = 5, IdUsuario = 6, SessionId = "sesion-sintetica",
        AttemptIdVeriff = "intento-sintetico", DecisionTimeVeriff = DecisionUtc, EsActual = true,
        EstadoKyc = "APROBADO", Proveedor = "VERIFF", Decision = "approved" };
    private static VeriffDecisionParsed Decision() => new("sesion-sintetica", "intento-sintetico", "approved", null,
        DecisionUtc, null, "ANA PRUEBA", "DOS APELLIDOS", "ID_CARD", "1.234.567.890", new(1990, 1, 1), "CO");

    [Theory]
    [InlineData("sesion")]
    [InlineData("intento")]
    [InlineData("tiempo")]
    [InlineData("sin_tiempo")]
    [InlineData("decision")]
    [InlineData("nombre")]
    [InlineData("apellido")]
    [InlineData("tipo")]
    [InlineData("numero_canonico_pero_raw_distinto")]
    [InlineData("vendor")]
    public void Prueba_distinta_o_incompleta_no_puede_reparar_identidad(string cambio)
    {
        var d = Decision();
        d = cambio switch {
            "sesion" => d with { SessionId = "otra-sesion" }, "intento" => d with { AttemptId = "otro-intento" },
            "tiempo" => d with { DecisionTimeUtc = DecisionUtc.AddSeconds(-1) }, "sin_tiempo" => d with { DecisionTimeUtc = null },
            "decision" => d with { Decision = "declined" }, "nombre" => d with { FirstName = null },
            "apellido" => d with { LastName = "OTRO" }, "tipo" => d with { DocumentType = "PASSPORT" },
            "numero_canonico_pero_raw_distinto" => d with { DocumentNumber = "1234567890" },
            "vendor" => d with { VendorData = "otro-titular" }, _ => throw new Exception() };
        Assert.False(SincronizacionDocumentoVeriffService.DecisionCoincide(Sesion(), Persona(), d));
    }

    [Fact]
    public void Evidencia_exacta_coincide_pero_sesion_ajena_o_historica_no()
    {
        Assert.True(SincronizacionDocumentoVeriffService.DecisionCoincide(Sesion(), Persona(), Decision()));
        var ajena = Sesion(); ajena.IdPersona = 99;
        Assert.False(SincronizacionDocumentoVeriffService.DecisionCoincide(ajena, Persona(), Decision()));
        var historica = Sesion(); historica.EsActual = false;
        Assert.False(SincronizacionDocumentoVeriffService.DecisionCoincide(historica, Persona(), Decision()));
    }

    [Fact]
    public async Task Feature_cerrado_no_consulta_SQL_ni_Veriff()
    {
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer("Server=unused.invalid;Database=synthetic;Integrated Security=true;Encrypt=true").Options);
        var config = new ConfigurationBuilder().Build();
        var ambiente = Common.AmbienteXpayTests.Ambiente("Production");
        var consentimiento = new ConsentimientoBiometricoService(db, config, ambiente, TimeProvider.System);
        var fake = new HttpFake(db);
        var kyc = new KycService(db, config, fake, NullLogger<KycService>.Instance, ambiente, consentimiento);
        var servicio = new SincronizacionDocumentoVeriffService(db, new(config, ambiente), consentimiento, kyc, TimeProvider.System);
        await Assert.ThrowsAsync<PreparacionWalletNoDisponibleException>(() => servicio.SincronizarAsync(6));
        Assert.Equal(0, fake.Peticiones);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Cliente_existente_hace_GET_autenticado_sin_SQL_y_preserva_raw_y_pais()
    {
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer("Server=unused.invalid;Database=synthetic;Integrated Security=true;Encrypt=true").Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["VERIFF_API_KEY"]="clave-sintetica", ["VERIFF_SHARED_SECRET"]="secreto-sintetico",
            ["VERIFF_BASE_URL"]="https://veriff.example.test" }).Build();
        var fake = new HttpFake(db);
        var servicio = new KycService(db, config, fake, NullLogger<KycService>.Instance,
            Common.AmbienteXpayTests.Ambiente("Production"));
        Assert.Equal(0, fake.Peticiones);
        var resultado = await servicio.FetchVeriffDecisionAsync("sesion-sintetica", CancellationToken.None);
        Assert.True(resultado.Exito);
        Assert.Equal(1, fake.Peticiones);
        Assert.Equal("1.234.567.890", resultado.Decision!.DocumentNumber);
        Assert.Equal("CO", resultado.Decision.DocumentCountry);
        Assert.Equal("1234567890", TipoDocumentoVeriffMapper.Mapear(resultado.Decision.DocumentType,
            resultado.Decision.DocumentCountry, resultado.Decision.DocumentNumber).NumeroDocumento);
        Assert.Equal("1.234.567.890", resultado.Decision.DocumentNumber);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private sealed class HttpFake(XpayDbContext db) : HttpMessageHandler, IHttpClientFactory
    {
        public int Peticiones { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler:false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ++Peticiones;
            Assert.Null(db.Database.CurrentTransaction);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://veriff.example.test/v1/sessions/sesion-sintetica/decision", request.RequestUri!.AbsoluteUri);
            Assert.True(request.Headers.Contains("X-AUTH-CLIENT"));
            Assert.True(request.Headers.Contains("X-HMAC-SIGNATURE"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                status="success", verification=new { id="sesion-sintetica", attemptId="intento-sintetico", status="approved",
                    decisionTime="2026-11-21T12:00:00Z", person=new { firstName="ANA PRUEBA", lastName="DOS APELLIDOS", dateOfBirth="1990-01-01" },
                    document=new { type="ID_CARD", country="CO", number="1.234.567.890" } } })) });
        }
    }
}
