using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Common;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — validaciones de TokenUsuarioService previas a la BD. El servicio se
// construye con db = null! (mismo recurso que QaClientePruebaServiceTests):
// cualquier acceso a la base lanzaría NullReferenceException, así que un
// resultado o una excepción de validación prueban que no se tocó la BD.
public class TokenUsuarioServiceValidacionTests
{
    private const string Verificar = PropositoTokenUsuario.VerificarEmail;
    private const string EmailOk   = "usuario@example.com";
    private const string Alfabeto  = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    private static TokenUsuarioService Servicio(Dictionary<string, string?>? config = null)
        => new(null!,
               new ConfigurationBuilder().AddInMemoryCollection(config ?? new()).Build(),
               TimeProvider.System);

    private static string TokenCanonico() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ── EmitirAsync ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Emitir_IdUsuarioNoPositivo_Rechaza(long idUsuario)
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Servicio().EmitirAsync(idUsuario, Verificar, EmailOk, null));

    [Theory]
    [InlineData("OTRO")]
    [InlineData("")]
    [InlineData("verificar_email")]
    [InlineData("Recuperar_Clave")]
    public async Task Emitir_PropositoInvalidoOCasingDistinto_Rechaza(string proposito)
        => await Assert.ThrowsAsync<ArgumentException>(
            () => Servicio().EmitirAsync(1, proposito, EmailOk, null));

    [Theory]
    [InlineData(" usuario@example.com")]
    [InlineData("Usuario@Example.com")]
    public async Task Emitir_EmailNoNormalizado_Rechaza(string email)
        => await Assert.ThrowsAsync<ArgumentException>(
            () => Servicio().EmitirAsync(1, Verificar, email, null));

    [Theory]
    [InlineData("no-es-email")]
    [InlineData("")]
    public async Task Emitir_EmailInvalido_Rechaza(string email)
        => await Assert.ThrowsAsync<ArgumentException>(
            () => Servicio().EmitirAsync(1, Verificar, email, null));

    [Theory]
    [InlineData("TokensUsuario:VerificarEmailMinutos", "0", PropositoTokenUsuario.VerificarEmail)]
    [InlineData("TokensUsuario:VerificarEmailMinutos", "10081", PropositoTokenUsuario.VerificarEmail)]
    [InlineData("TokensUsuario:VerificarEmailMinutos", "abc", PropositoTokenUsuario.VerificarEmail)]
    [InlineData("TokensUsuario:RecuperarClaveMinutos", "0", PropositoTokenUsuario.RecuperarClave)]
    [InlineData("TokensUsuario:RecuperarClaveMinutos", "121", PropositoTokenUsuario.RecuperarClave)]
    [InlineData("TokensUsuario:RecuperarClaveMinutos", "-5", PropositoTokenUsuario.RecuperarClave)]
    public async Task Emitir_VigenciaFueraDeRangoOInvalida_FailClosed(string clave, string valor, string proposito)
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => Servicio(new() { [clave] = valor }).EmitirAsync(1, proposito, EmailOk, null));

    // ── ConsumirAsync ───────────────────────────────────────────────────

    [Theory]
    [InlineData("OTRO")]
    [InlineData("verificar_email")]
    public async Task Consumir_PropositoInvalido_Rechaza(string proposito)
        => await Assert.ThrowsAsync<ArgumentException>(
            () => Servicio().ConsumirAsync(TokenCanonico(), proposito));

    public static TheoryData<string?> TokensMalFormados()
    {
        var a42 = new string('A', 42);
        return new TheoryData<string?>
        {
            null,
            a42,                 // 42 caracteres
            a42 + "AA",          // 44 caracteres
            a42 + "=",
            a42 + "+",
            a42 + "/",
            a42 + " ",
            a42 + "\n",
            a42 + "ñ",           // Unicode
        };
    }

    [Theory]
    [MemberData(nameof(TokensMalFormados))]
    public async Task Consumir_TokenMalFormado_NullSinTocarBd(string? token)
        => Assert.Null(await Servicio().ConsumirAsync(token, Verificar));

    [Fact]
    public async Task Consumir_TokenNoCanonico_NullSinTocarBd()
    {
        // Los 2 bits bajos del último carácter deben ser 0 en la forma
        // canónica; con +1 decodifica a los mismos 32 bytes pero se rechaza.
        var canonico = TokenCanonico();
        var indice   = Alfabeto.IndexOf(canonico[^1]);
        var variante = canonico[..^1] + Alfabeto[indice + 1];

        Assert.Null(await Servicio().ConsumirAsync(variante, Verificar));

        // Control: el canónico sí supera la validación y llega a la BD (null!).
        await Assert.ThrowsAsync<NullReferenceException>(
            () => Servicio().ConsumirAsync(canonico, Verificar));
    }

    // ── Privacidad de ToString ──────────────────────────────────────────

    [Fact]
    public void TokenEmitido_ToString_NoRevelaElToken()
    {
        var token = TokenCanonico();
        var texto = new TokenEmitido(token, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)).ToString();
        Assert.True(texto.IndexOf(token, StringComparison.Ordinal) < 0);
        Assert.Contains("Token = ***", texto);
    }

    [Fact]
    public void TokenConsumido_ToString_NoRevelaElEmail()
    {
        var texto = new TokenConsumido(7, EmailOk).ToString();
        Assert.True(texto.IndexOf(EmailOk, StringComparison.Ordinal) < 0);
        Assert.Contains("EmailDestino = ***", texto);
        Assert.Contains("IdUsuario = 7", texto);
    }
}
