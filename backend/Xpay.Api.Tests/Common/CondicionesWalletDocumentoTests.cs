using System.Security.Cryptography;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

public sealed class CondicionesWalletDocumentoTests
{
    [Fact]
    public void Huella_del_anexo_exacto_coincide_con_el_archivo_que_servira_la_interfaz()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        const string relativo = "frontend/xpay-admin/public/legal/wallet/v1.0/condiciones.pdf";
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, relativo))) raiz = raiz.Parent;
        Assert.NotNull(raiz);
        var pdf = File.ReadAllBytes(Path.Combine(raiz!.FullName, relativo));
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        Assert.Equal(CondicionesWalletPiloto.Sha256, Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant());
    }
}
