using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class PrimerApellidoDeclaradoReglasTests
{
    [Theory]
    [InlineData("De la Hoz", "DE LA HOZ GARCÍA", true)]
    [InlineData("Garci\u0301a", "GARCÍA LÓPEZ", true)]
    [InlineData("Pérez", "PÉREZ", true)]
    [InlineData("Pérez", "PEREZ GÓMEZ", false)]
    [InlineData("Pin", "PINEDA GÓMEZ", false)]
    [InlineData("Hoz", "DE LA HOZ GARCÍA", false)]
    [InlineData("Gómez", null, false)]
    public void Contraste_ConservaApellidoCompuestoYTildes_YExigeLimiteDePalabra(
        string declarado, string? completo, bool coincide)
        => Assert.Equal(coincide, PrimerApellidoDeclaradoReglas.CoincideConCompleto(declarado, completo));

    [Fact]
    public void SiHaySegundoApellidoDeclarado_DebeCoincidirTodoElConjunto()
    {
        Assert.True(PrimerApellidoDeclaradoReglas.CoincideConCompleto("De la Hoz", "DE LA HOZ GARCÍA", "García"));
        Assert.False(PrimerApellidoDeclaradoReglas.CoincideConCompleto("De la Hoz", "DE LA HOZ GARCÍA", "Otro"));
    }

    private static (Usuario Usuario, Persona Persona, List<KycVerificacion> Sesiones) Validos()
    {
        var usuario = new Usuario { IdUsuario = 11, IdPersona = 22, Estado = "ACTIVO", EmailVerificado = true, EstadoKycActual = "APROBADO" };
        var persona = new Persona { IdPersona = 22, Estado = "ACTIVA", Email = "persona@example.test", IdentidadVerificada = true,
            IdentidadVerificadaProveedor = "VERIFF", ApellidoVerificadoCompleto = "DE LA HOZ GARCÍA" };
        var sesiones = new List<KycVerificacion> { new() { IdUsuario = 11, IdPersona = 22, EsActual = true, Proveedor = "VERIFF", EstadoKyc = "APROBADO" } };
        return (usuario, persona, sesiones);
    }

    [Fact]
    public void CuentaPropiaConCorreoEIdentidadVigente_PuedeCompletar()
    {
        var x = Validos();
        Assert.True(PrimerApellidoDeclaradoReglas.TieneIdentidadVigente(x.Usuario, x.Persona, x.Sesiones));
    }

    [Theory]
    [InlineData("otroTitular")]
    [InlineData("usuarioInactivo")]
    [InlineData("personaInactiva")]
    [InlineData("sinCorreo")]
    [InlineData("correoNoVerificado")]
    [InlineData("clavePendiente")]
    [InlineData("kycPendiente")]
    [InlineData("identidadNoVerificada")]
    [InlineData("identidadSimulada")]
    [InlineData("sinApellidoVerificado")]
    [InlineData("ceroSesiones")]
    [InlineData("dosSesiones")]
    [InlineData("sesionOtroUsuario")]
    [InlineData("sesionOtraPersona")]
    [InlineData("sesionNoActual")]
    [InlineData("sesionSimulada")]
    [InlineData("sesionNoAprobada")]
    public void NoAutorizaSinCondicionesDelTitular(string caso)
    {
        var x = Validos();
        switch (caso)
        {
            case "otroTitular": x.Usuario.IdPersona = 23; break;
            case "usuarioInactivo": x.Usuario.Estado = "BLOQUEADO"; break;
            case "personaInactiva": x.Persona.Estado = "INACTIVA"; break;
            case "sinCorreo": x.Persona.Email = null; break;
            case "correoNoVerificado": x.Usuario.EmailVerificado = false; break;
            case "clavePendiente": x.Usuario.RequiereCambioClave = true; break;
            case "kycPendiente": x.Usuario.EstadoKycActual = "PENDIENTE"; break;
            case "identidadNoVerificada": x.Persona.IdentidadVerificada = false; break;
            case "identidadSimulada": x.Persona.IdentidadVerificadaProveedor = "SIMULACION_QA"; break;
            case "sinApellidoVerificado": x.Persona.ApellidoVerificadoCompleto = null; break;
            case "ceroSesiones": x.Sesiones.Clear(); break;
            case "dosSesiones": x.Sesiones.Add(new()); break;
            case "sesionOtroUsuario": x.Sesiones[0].IdUsuario = 12; break;
            case "sesionOtraPersona": x.Sesiones[0].IdPersona = 23; break;
            case "sesionNoActual": x.Sesiones[0].EsActual = false; break;
            case "sesionSimulada": x.Sesiones[0].Proveedor = "SIMULACION_QA"; break;
            case "sesionNoAprobada": x.Sesiones[0].EstadoKyc = "PENDIENTE"; break;
        }
        Assert.False(PrimerApellidoDeclaradoReglas.TieneIdentidadVigente(x.Usuario, x.Persona, x.Sesiones));
    }
}
