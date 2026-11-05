using System.Security.Cryptography;
using System.Text;
using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

// P0-A — fail-fast de Jwt:Key. Todas las claves son ficticias/aleatorias
// generadas aquí; la clave comprometida real NUNCA aparece en este archivo:
// su rechazo se demuestra con una clave ficticia y su propia huella SHA-256.
public class JwtKeyGuardTests
{
    private static string Aleatoria(int bytesAleatorios) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytesAleatorios));

    private static string Huella(string clave) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ausente_O_EnBlanco_SeRechaza(string? clave)
        => Assert.Throws<InvalidOperationException>(() => JwtKeyGuard.Validar(clave));

    [Fact]
    public void MenosDe32BytesUtf8_SeRechaza_SinRevelarLaClave()
    {
        var corta = new string('k', 31);
        var ex = Assert.Throws<InvalidOperationException>(() => JwtKeyGuard.Validar(corta));
        Assert.DoesNotContain(corta, ex.Message);
    }

    [Fact]
    public void CuentaBytesNoCaracteres()
    {
        // 'ñ' ocupa 2 bytes UTF-8: 15 'ñ' + 'k' = 16 caracteres pero 31 bytes.
        Assert.Throws<InvalidOperationException>(() => JwtKeyGuard.Validar(new string('ñ', 15) + "k"));
        // 16 'ñ' = 16 caracteres y exactamente 32 bytes: se acepta.
        JwtKeyGuard.Validar(new string('ñ', 16));
    }

    [Fact]
    public void Exactamente32Bytes_SeAcepta()
    {
        var clave = Aleatoria(24); // base64 de 24 bytes = 32 caracteres ASCII
        Assert.Equal(32, Encoding.UTF8.GetByteCount(clave));
        JwtKeyGuard.Validar(clave);
    }

    [Fact]
    public void MasDe32Bytes_SeAcepta()
        => JwtKeyGuard.Validar(Aleatoria(48));

    [Fact]
    public void ClaveComprometida_SeRechazaSiempre_SinRevelarla()
    {
        // Clave ficticia válida por longitud, declarada "comprometida" vía su huella.
        var ficticia = Aleatoria(48);
        var ex = Assert.Throws<InvalidOperationException>(() => JwtKeyGuard.Validar(ficticia, Huella(ficticia)));
        Assert.Contains("comprometida", ex.Message);
        Assert.DoesNotContain(ficticia, ex.Message);
        Assert.DoesNotContain(Huella(ficticia), ex.Message);
    }

    [Fact]
    public void ValidarPublico_UsaLaHuellaReal_YAceptaClavesNoComprometidas()
    {
        Assert.Matches("^[0-9A-F]{64}$", JwtKeyGuard.HuellaClaveComprometida);
        Assert.Equal(32, Convert.FromHexString(JwtKeyGuard.HuellaClaveComprometida).Length);

        var nueva = Aleatoria(48);
        Assert.False(JwtKeyGuard.EsClaveComprometida(nueva, JwtKeyGuard.HuellaClaveComprometida));
        JwtKeyGuard.Validar(nueva);
    }
}
