using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// XPAY-400 — pruebas ESTRUCTURALES (reflexión), sin XpayDbContext, sin
// host, sin red — mismo criterio que PerfilContractTests.cs (XPAY-399):
// no existe infraestructura de pruebas de integración con base de datos en
// este proyecto, así que autorización/IDOR/forma del DTO se verifican de
// forma estática. Las reglas que sí requieren ejecutar
// AuthService.CambiarClaveVoluntariaAsync contra una base de datos real
// (auditoría creada en éxito, RequiereCambioClave no alterado, hash
// actualizado) quedan garantizadas por revisión de código — ver comentarios
// en AuthService.cs — y NO se afirman aquí como "probadas por test
// ejecutable".
public class CambiarClaveContractTests
{
    private static MethodInfo Metodo(string nombre) =>
        typeof(AuthController).GetMethod(nombre)
        ?? throw new InvalidOperationException($"No se encontró AuthController.{nombre}");

    [Fact]
    public void CambiarClave_TieneAuthorizeSimple_NoSoloAutenticado()
    {
        var metodo = Metodo(nameof(AuthController.CambiarClave));
        var authorize = metodo.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        // [Authorize] simple (Policy == null) hereda la DefaultPolicy de
        // Program.cs, que incluye ClaveVigenteRequirement — es justamente
        // ESO lo que impide saltarse el flujo obligatorio (XPAY-400 PASO 5).
        // Si algún día este atributo cambiara a Policy="SoloAutenticado",
        // este test lo detectaría como una regresión de seguridad real.
        Assert.Null(authorize!.Policy);
        Assert.NotNull(metodo.GetCustomAttribute<HttpPostAttribute>());
    }

    [Fact]
    public void CambiarClave_TieneRateLimitingPropio_NoLoginPolicy()
    {
        var metodo = Metodo(nameof(AuthController.CambiarClave));
        var rateLimit = metodo.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(rateLimit);
        Assert.Equal("CambiarClavePolicy", rateLimit!.PolicyName);
        Assert.NotEqual("LoginPolicy", rateLimit.PolicyName); // XPAY-400 PASO 6 — no reutilizado sin criterio
    }

    [Fact]
    public void CambiarClave_UnicoParametroEsElDtoPermitido()
    {
        var parametros = Metodo(nameof(AuthController.CambiarClave)).GetParameters();
        var parametro = Assert.Single(parametros);
        Assert.Equal(typeof(CambiarClaveRequest), parametro.ParameterType);
        // Ningún parámetro long/string adicional que permitiera indicar
        // "otro" usuario (IDOR) — idUsuario se resuelve internamente desde
        // el JWT (ver AuthController.CambiarClave).
    }

    [Fact]
    public void CambiarClaveRequest_SoloExponeClaveActualYClaveNueva()
    {
        var propiedades = typeof(CambiarClaveRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "ClaveActual", "ClaveNueva" }, propiedades);
    }

    [Theory]
    [InlineData("IdUsuario")]
    [InlineData("IdPersona")]
    [InlineData("Usuario")]
    [InlineData("NombreUsuario")]
    [InlineData("Email")]
    [InlineData("Roles")]
    [InlineData("ConfirmacionClaveNueva")] // a propósito, distinto del flujo obligatorio — contrato mínimo del ticket
    public void CambiarClaveRequest_NuncaAceptaIdentidadNiConfirmacion(string campoProhibido)
    {
        var propiedades = typeof(CambiarClaveRequest).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(campoProhibido, propiedades);
    }

    // ── Regresión: el flujo OBLIGATORIO no debe haber sido tocado ──────────
    [Fact]
    public void CambiarClaveObligatoria_SigueUsandoPolicySoloAutenticado()
    {
        var metodo = Metodo(nameof(AuthController.CambiarClaveObligatoria));
        var authorize = metodo.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("SoloAutenticado", authorize!.Policy);
    }

    [Fact]
    public void CambiarClaveObligatoria_NoTieneRateLimitingNuevo()
    {
        var metodo = Metodo(nameof(AuthController.CambiarClaveObligatoria));
        Assert.Null(metodo.GetCustomAttribute<EnableRateLimitingAttribute>());
    }
}
