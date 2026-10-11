using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

// XPAY-712 - hash AVANCE_WALLET. Pura, sin red ni BD.
public sealed class IdempotencyHashHelperAvanceWalletTests
{
    private static byte[] H(
        long u = 10,
        string t = "AVANCE_WALLET",
        decimal v = 100000m,
        int p = 1,
        string f = "MENSUAL")
        => IdempotencyHashHelper.ComputeAvanceWalletHash(u, t, v, p, f);

    [Fact]
    public void Mismo_payload_mismo_hash_32_bytes()
    {
        Assert.Equal(H(), H());
        Assert.Equal(32, H().Length);
    }

    [Fact]
    public void Normaliza_tipo()
    {
        Assert.Equal(H(), H(t: " avance_wallet "));
    }

    [Fact]
    public void Normaliza_frecuencia()
    {
        Assert.Equal(H(), H(f: " Mensual "));
    }

    [Fact]
    public void Decimal_equivalente_mismo_hash()
    {
        Assert.Equal(H(v: 100000m), H(v: 100000.00m));
    }

    [Fact]
    public void Decimal_distinto_cambia_hash()
    {
        Assert.NotEqual(H(), H(v: 100000.01m));
    }

    [Fact]
    public void Usuario_distinto_cambia_hash()
    {
        Assert.NotEqual(H(), H(u: 11));
    }

    [Fact]
    public void Valor_distinto_cambia_hash()
    {
        Assert.NotEqual(H(), H(v: 200000m));
    }

    [Fact]
    public void Plazo_distinto_cambia_hash()
    {
        Assert.NotEqual(H(), H(p: 2));
    }

    [Fact]
    public void Frecuencia_distinta_cambia_hash()
    {
        Assert.NotEqual(H(), H(f: "QUINCENAL"));
    }

    [Fact]
    public void Tipo_distinto_cambia_hash()
    {
        Assert.NotEqual(H(), H(t: "COMPRA_COMERCIO"));
    }
}
