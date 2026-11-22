using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

public sealed class CondicionesWalletDocumentoTests
{
    [Theory]
    [InlineData("terminos.pdf")]
    [InlineData("politica.pdf")]
    [InlineData("autorizaciones.pdf")]
    public void HuellaDeCadaDocumento13_CoincideConElPdfQueServiraLaInterfaz(string archivo)
    {
        var raiz = new DirectoryInfo(Path.GetDirectoryName(ArchivoFuente())!);
        var relativo = "frontend/xpay-admin/public/legal/v1.3/" + archivo;
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, relativo))) raiz = raiz.Parent;
        Assert.NotNull(raiz);
        var pdf = File.ReadAllBytes(Path.Combine(raiz!.FullName, relativo));
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        var esperado = archivo switch {
            "terminos.pdf" => PreparacionWalletConfiguracion.TerminosSha256,
            "politica.pdf" => PreparacionWalletConfiguracion.PoliticaDatosSha256,
            _ => PreparacionWalletConfiguracion.AutorizacionSha256
        };
        Assert.Equal(64, esperado.Length);
        Assert.Equal(esperado, Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant());
    }

    [Fact]
    public void CasillaBiometricaConservaExactamenteElTextoAceptadoEn12()
    {
        var sha = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ConsentimientoBiometricoService.TextoCasilla))).ToLowerInvariant();
        Assert.Equal("3e9dd3ff62d40a407499e34405194ad4a4fe8ed47978a132cb1c6c73b20075d9", sha);
    }

    private static string ArchivoFuente([CallerFilePath] string archivo = "") => archivo;
}
