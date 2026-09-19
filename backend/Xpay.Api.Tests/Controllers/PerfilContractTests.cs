using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// XPAY-399 (Perfil Fase 2A) — pruebas ESTRUCTURALES (reflexión), sin
// XpayDbContext, sin host, sin red: no existe infraestructura de pruebas de
// integración con base de datos en este proyecto (ver PASO 10 del ticket —
// "por el bloqueo de entorno conocido" y "no operaciones externas / no QA
// DB"), así que la protección contra overposting/IDOR y la presencia de
// [Authorize] se verifican de forma estática, la misma garantía que daría
// un test de integración para ESTAS propiedades concretas (forma del DTO,
// atributos del método) sin necesitar una base de datos real.
public class PerfilContractTests
{
    private static MethodInfo Metodo(string nombre) =>
        typeof(UsuariosController).GetMethod(nombre)
        ?? throw new InvalidOperationException($"No se encontró UsuariosController.{nombre}");

    // ── GET/PATCH requieren autenticación ────────────────────────────────────

    [Fact]
    public void ObtenerMiPerfil_TieneAuthorize()
    {
        var metodo = Metodo(nameof(UsuariosController.ObtenerMiPerfil));
        Assert.NotNull(metodo.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(metodo.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void ActualizarMiPerfil_TieneAuthorize()
    {
        var metodo = Metodo(nameof(UsuariosController.ActualizarMiPerfil));
        Assert.NotNull(metodo.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(metodo.GetCustomAttribute<HttpPatchAttribute>());
    }

    // ── Protección IDOR: el cliente no puede indicar qué persona/usuario
    //    leer o modificar — ninguno de los dos métodos acepta un parámetro
    //    de tipo numérico (idPersona/idUsuario) proveniente del cliente. ──
    [Fact]
    public void ObtenerMiPerfil_NoAceptaNingunParametroDelCliente()
    {
        var parametros = Metodo(nameof(UsuariosController.ObtenerMiPerfil)).GetParameters();
        Assert.Empty(parametros); // idPersona se resuelve internamente desde el JWT, nunca desde la firma del método
    }

    [Fact]
    public void ActualizarMiPerfil_UnicoParametroEsElDtoPermitido()
    {
        var parametros = Metodo(nameof(UsuariosController.ActualizarMiPerfil)).GetParameters();
        var parametro = Assert.Single(parametros);
        Assert.Equal(typeof(ActualizarMiPerfilRequest), parametro.ParameterType);
        // Ningún parámetro adicional de tipo long/string que pudiera usarse
        // para seleccionar "otra" persona/usuario (IDOR).
    }

    // ── Overposting: ActualizarMiPerfilRequest solo puede transportar los
    //    5 campos autorizados — ningún otro campo (identidad legal, KYC,
    //    seguridad) puede llegar por este DTO sin importar qué envíe el
    //    cliente en el JSON crudo. ──
    [Fact]
    public void ActualizarMiPerfilRequest_SoloExponeLosCincoCamposAutorizados()
    {
        var propiedades = typeof(ActualizarMiPerfilRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "Celular", "Ciudad", "Departamento", "Direccion", "Email" },
            propiedades);
    }

    [Theory]
    [InlineData("PrimerNombre")]
    [InlineData("SegundoNombre")]
    [InlineData("PrimerApellido")]
    [InlineData("SegundoApellido")]
    [InlineData("TipoDocumento")]
    [InlineData("NumeroDocumento")]
    [InlineData("FechaNacimiento")]
    [InlineData("Pais")]
    [InlineData("IdentidadVerificada")]
    [InlineData("IdentidadVerificadaProveedor")]
    [InlineData("NombreVerificadoCompleto")]
    [InlineData("ApellidoVerificadoCompleto")]
    [InlineData("TipoDocumentoVeriffRaw")]
    [InlineData("NumeroDocumentoVerificado")]
    [InlineData("EstadoKycActual")]
    [InlineData("EmailVerificado")]
    [InlineData("CelularVerificado")]
    [InlineData("PasswordHash")]
    [InlineData("RequiereCambioClave")]
    [InlineData("Estado")]
    [InlineData("NombreUsuario")]
    [InlineData("Roles")]
    public void ActualizarMiPerfilRequest_JamasExponeIdentidadLegalKycOSeguridad(string campoProhibido)
    {
        var propiedades = typeof(ActualizarMiPerfilRequest).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(campoProhibido, propiedades);
    }

    // ── El DTO de respuesta nunca expone crudo de Veriff/tokens/secretos ──
    [Theory]
    [InlineData("SessionId")]
    [InlineData("SessionUrl")]
    [InlineData("VendorData")]
    [InlineData("Reason")]
    [InlineData("Decision")]
    [InlineData("PasswordHash")]
    [InlineData("Token")]
    [InlineData("IdentidadVerificadaProveedor")]
    [InlineData("NombreVerificadoCompleto")]
    [InlineData("ApellidoVerificadoCompleto")]
    [InlineData("TipoDocumentoVeriffRaw")]
    [InlineData("NumeroDocumentoVerificado")]
    public void MiPerfilResponseDto_NuncaExponeCrudoDeVeriffNiSecretos(string campoProhibido)
    {
        var propiedades = typeof(MiPerfilResponseDto).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(campoProhibido, propiedades);
    }

    [Fact]
    public void MiPerfilResponseDto_ExponeSoloUnResumenBooleanoTextualDeIdentidad()
    {
        var propiedades = typeof(MiPerfilResponseDto).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("IdentidadVerificada", propiedades); // bool — resumen, no el detalle crudo
        Assert.Contains("EstadoKycActual", propiedades);     // string — resumen de usuarios.estado_kyc_actual
    }
}
