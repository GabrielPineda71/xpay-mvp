using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

// P0-B — EmailNormalizador: Trim + minúsculas, sin reglas de proveedor, solo
// ASCII imprimible y máximo 200 caracteres (personas.email VARCHAR(200)).
public class EmailNormalizadorTests
{
    // Dominio de 135 caracteres con etiquetas <= 63: 63 + 1 + 63 + 1 + 4 + 3.
    private static string Dominio(int relleno) =>
        new string('d', 63) + "." + new string('e', 63) + "." + new string('f', relleno) + ".co";

    [Fact]
    public void Normaliza_TrimYMinusculas_ConservaPuntoYAlias()
    {
        Assert.True(EmailNormalizador.TryNormalizar("  User.Name+tag@Example.COM ", out var email));
        Assert.Equal("user.name+tag@example.com", email);
    }

    [Fact]
    public void Acepta_Exactamente200Caracteres()
    {
        var entrada = new string('a', 64) + "@" + Dominio(4);
        Assert.Equal(200, entrada.Length);
        Assert.True(EmailNormalizador.TryNormalizar(entrada, out var email));
        Assert.Equal(entrada, email);
    }

    [Fact]
    public void Acepta_ParteLocalDeExactamente64()
    {
        var entrada = new string('a', 64) + "@x.co";
        Assert.True(EmailNormalizador.TryNormalizar(entrada, out var email));
        Assert.Equal(entrada, email);
    }

    [Fact]
    public void Rechaza_201Caracteres()
    {
        var entrada = new string('a', 64) + "@" + Dominio(5);
        Assert.Equal(201, entrada.Length);
        AssertRechazado(entrada);
    }

    [Fact]
    public void Rechaza_ParteLocalDe65()
        => AssertRechazado(new string('a', 65) + "@x.co");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ax.co")]          // sin @
    [InlineData("a@b@x.co")]       // dos @
    [InlineData("@x.co")]
    [InlineData("a@")]
    [InlineData("a@localhost")]    // dominio sin punto
    [InlineData("a@.x.co")]        // punto inicial
    [InlineData("a@x.co.")]        // punto final
    [InlineData("a@x..co")]
    [InlineData("a b@x.co")]       // espacio interno
    [InlineData("a\tb@x.co")]      // tabulador
    [InlineData("a\nb@x.co")]      // salto de línea
    [InlineData("ñandu@x.co")]
    [InlineData("a@müller.de")]
    [InlineData("A <a@b.co>")]     // display-name
    [InlineData("a@b.co,c@d.co")]  // múltiples direcciones
    public void Rechaza_EntradasInvalidas(string? entrada) => AssertRechazado(entrada);

    private static void AssertRechazado(string? entrada)
    {
        Assert.False(EmailNormalizador.TryNormalizar(entrada, out var email));
        Assert.Equal(string.Empty, email);
    }
}
