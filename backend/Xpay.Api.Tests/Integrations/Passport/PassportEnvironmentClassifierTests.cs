using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-385 FASE 7 test #7 — SANDBOX no se confunde con PRODUCCION.
// Función PURA, sin I/O, sin red.
public class PassportEnvironmentClassifierTests
{
    [Fact]
    public void Clasificar_HostSandboxConocido_EsSandbox() =>
        Assert.Equal("SANDBOX",
            PassportEnvironmentClassifier.Clasificar("https://api.paas.sandbox.co.passportfintech.com"));

    [Fact]
    public void Clasificar_HostSandbox_EsCaseInsensitive() =>
        Assert.Equal("SANDBOX",
            PassportEnvironmentClassifier.Clasificar("https://API.PAAS.SANDBOX.CO.PASSPORTFINTECH.COM"));

    [Theory]
    [InlineData("https://api.passportfintech.com")]                 // host de producción documentado (a confirmar)
    [InlineData("https://otro-host-cualquiera.example.com")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no-es-una-url-valida")]
    public void Clasificar_CualquierOtroHost_EsProduccion_NuncaSandboxPorDefecto(string? baseUrl) =>
        Assert.Equal("PRODUCCION", PassportEnvironmentClassifier.Clasificar(baseUrl));

    [Fact]
    public void Clasificar_HostSandboxConPathYQuery_SigueSiendoSandbox() =>
        Assert.Equal("SANDBOX",
            PassportEnvironmentClassifier.Clasificar("https://api.paas.sandbox.co.passportfintech.com/v1/payments/breb"));
}
