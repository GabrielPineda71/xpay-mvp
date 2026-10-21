using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — DTO público de registro y su validación: sin identificadores
// internos, email obligatorio y normalizado, y la MISMA política de
// contraseña que AuthService (sin una segunda política divergente).
public class RegistroPublicoValidacionTests
{
    private const string ClaveValida = "Segura#2026x";

    private static RegistroPublicoRequest Request(
        string? email = " Persona.Uno+tag@Example.COM ", string usuario = " Persona_Uno ",
        string password = ClaveValida, string celular = " 3001234567 ")
        => new(email!, usuario, password, celular);

    private static RegistroPublicoNormalizado Validar(RegistroPublicoRequest r, string ambiente = "Production")
        => RegistroPublicoValidacion.Validar(r, AmbienteXpayTests.Ambiente(ambiente));

    [Fact]
    public void Dto_SoloTieneLosCuatroCamposPublicos()
    {
        var propiedades = typeof(RegistroPublicoRequest).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(new[] { "Celular", "Email", "NombreUsuario", "Password" }, propiedades);

        var parametros = typeof(RegistroPublicoRequest).GetConstructors().Single().GetParameters().Select(p => p.Name!);
        Assert.Equal(new[] { "Email", "NombreUsuario", "Password", "Celular" }, parametros);
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Rol")]
    [InlineData("Unidad")]
    [InlineData("Empresa")]
    [InlineData("Comercio")]
    [InlineData("Tenant")]
    [InlineData("Organizacion")]
    [InlineData("Convenio")]
    public void Dto_NoContieneIdentificadoresInternos(string fragmento)
        => Assert.DoesNotContain(typeof(RegistroPublicoRequest).GetProperties(),
            p => p.Name.Contains(fragmento, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Dto_ToString_NoRevelaPasswordNiEmail()
    {
        var texto = Request().ToString();
        Assert.True(texto.IndexOf(ClaveValida, StringComparison.Ordinal) < 0);
        Assert.True(texto.IndexOf("Persona.Uno", StringComparison.OrdinalIgnoreCase) < 0);
        Assert.Contains("Password = ***", texto);
    }

    [Fact]
    public void Valido_NormalizaEmailUsuarioYCelular()
    {
        var n = Validar(Request());
        Assert.Equal("persona.uno+tag@example.com", n.Email);
        Assert.Equal("persona_uno", n.NombreUsuario);
        Assert.Equal("3001234567", n.Celular);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmailObligatorio(string? email)
        => Assert.Equal("El email es obligatorio.",
            Assert.Throws<InvalidOperationException>(() => Validar(Request(email: email))).Message);

    [Theory]
    [InlineData("sin-arroba.example.com")]
    [InlineData("a@b@c.co")]
    [InlineData("ñandu@x.co")]
    public void EmailInvalido(string email)
        => Assert.Equal("El email no es válido.",
            Assert.Throws<InvalidOperationException>(() => Validar(Request(email: email))).Message);

    // La validación del registro debe fallar EXACTAMENTE igual que la política
    // existente de AuthService para la misma clave y el mismo usuario.
    [Theory]
    [InlineData("Ab1!")]
    [InlineData("minusculas1!")]
    [InlineData("MAYUSCULAS1!")]
    [InlineData("SinDigitos!!")]
    [InlineData("SinEspecial123")]
    [InlineData("Persona_Uno#2026")]
    public void Password_ReutilizaLaPoliticaDeAuthService(string clave)
    {
        var esperado = Assert.Throws<InvalidOperationException>(
            () => AuthService.ValidarPoliticaClave(clave, "persona_uno"));
        var real = Assert.Throws<InvalidOperationException>(() => Validar(Request(password: clave)));
        Assert.Equal(esperado.Message, real.Message);
    }

    [Fact]
    public void UsuarioQaReservado_EnProductivo_SeRechaza_EnQaSePermite()
    {
        var r = Request(usuario: "qa.persona");
        Assert.Equal(RegistroInicialService.MensajeUsuarioNoDisponible,
            Assert.Throws<InvalidOperationException>(() => Validar(r, "Production")).Message);
        Assert.Equal("qa.persona", Validar(r, "QA").NombreUsuario);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CelularObligatorio(string celular)
        => Assert.Equal("El celular es obligatorio.",
            Assert.Throws<InvalidOperationException>(() => Validar(Request(celular: celular))).Message);
}
