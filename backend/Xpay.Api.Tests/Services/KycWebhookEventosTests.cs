using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Veriff Station envía al mismo endpoint dos tipos de webhook: "events"
// (started/submitted, sin objeto verification) y "decisions". Un evento con
// firma válida se acepta (el controller responde 200) sin cambiar ningún
// estado ni tocar la base: se descarta antes de cualquier consulta.
public sealed class KycWebhookEventosTests
{
    private static KycService Servicio() => new(
        // Contexto sin servidor: si el evento llegara a la base, la prueba fallaría.
        new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=NoExiste;Connect Timeout=1;").Options),
        new ConfigurationBuilder().Build(), new SinHttp(), NullLogger<KycService>.Instance,
        Xpay.Api.Tests.Common.AmbienteXpayTests.Ambiente("PROD"));

    [Theory]
    [InlineData("started", 7001)]
    [InlineData("submitted", 7002)]
    public async Task Evento_sin_verification_se_descarta_sin_tocar_la_base(string accion, int codigo)
    {
        var body = $$"""
            {"id":"f04bdb47-d3be-4b28-b028-a652feb060b5","attemptId":"e30122d1-740b-4764-853f-470374a7abf4",
             "feature":"selfid","code":{{codigo}},"action":"{{accion}}","vendorData":"XPAY-PROD-USUARIO-42"}
            """;
        var r = await Servicio().ProcessVeriffWebhookAsync(body);
        Assert.False(r.Processed);
    }

    private sealed class SinHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("Estas pruebas no deben llamar a Veriff.");
    }
}
