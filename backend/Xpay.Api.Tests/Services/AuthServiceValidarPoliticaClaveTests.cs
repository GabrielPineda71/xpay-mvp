using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-400 — AuthService.ValidarPoliticaClave es la MISMA función pura
// (internal static, sin XpayDbContext) que ya usaba
// CambiarClaveObligatoriaAsync y que ahora también usa
// CambiarClaveVoluntariaAsync (XPAY-400) — cero duplicación de la política.
// Accesible desde este proyecto de tests vía InternalsVisibleTo
// (Xpay.Api.csproj, XPAY-400) — mecanismo estándar del SDK, sin
// dependencias nuevas, sin cambiar la visibilidad "internal" de producción.
//
// Cubre el caso XPAY-400 PASO 11 "nueva contraseña incumple política →
// rechazo": como esta es EXACTAMENTE la función que
// CambiarClaveVoluntariaAsync invoca para validar ClaveNueva, probarla
// directamente prueba esa regla del endpoint nuevo sin necesitar
// XpayDbContext.
public class AuthServiceValidarPoliticaClaveTests
{
    [Theory]
    [InlineData("corta1!A")]      // válida de referencia (8 chars, mayus/minus/digito/especial) — no debe lanzar
    public void ValidarPoliticaClave_ClaveValida_NoLanza(string clave)
    {
        var ex = Record.Exception(() => AuthService.ValidarPoliticaClave(clave, "usuario.demo"));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidarPoliticaClave_MuyCorta_Lanza()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AuthService.ValidarPoliticaClave("Ab1!", "usuario.demo"));
        Assert.Contains("8 y 128", ex.Message);
    }

    [Fact]
    public void ValidarPoliticaClave_SinMayuscula_Lanza()
    {
        Assert.Throws<InvalidOperationException>(() => AuthService.ValidarPoliticaClave("sinmayuscula1!", "usuario.demo"));
    }

    [Fact]
    public void ValidarPoliticaClave_SinMinuscula_Lanza()
    {
        Assert.Throws<InvalidOperationException>(() => AuthService.ValidarPoliticaClave("SINMINUSCULA1!", "usuario.demo"));
    }

    [Fact]
    public void ValidarPoliticaClave_SinDigito_Lanza()
    {
        Assert.Throws<InvalidOperationException>(() => AuthService.ValidarPoliticaClave("SinDigitoAqui!", "usuario.demo"));
    }

    [Fact]
    public void ValidarPoliticaClave_SinCaracterEspecial_Lanza()
    {
        Assert.Throws<InvalidOperationException>(() => AuthService.ValidarPoliticaClave("SinEspecial123", "usuario.demo"));
    }

    [Fact]
    public void ValidarPoliticaClave_ContieneNombreUsuario_Lanza()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AuthService.ValidarPoliticaClave("qa.usuario1Clave!1", "qa.usuario1"));
        Assert.Contains("nombre de usuario", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
