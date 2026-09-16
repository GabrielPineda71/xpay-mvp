using System.Reflection;
using Xpay.Api.Controllers;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// XPAY-377 FASE 6 test #9 — verificación ESTRUCTURAL (reflexión, sin
// instanciar el controller ni tocar DB/red) de que
// POST /api/breb/mis-retiros/{id}/actualizar-estado nunca puede recibir un
// payment_id (ni ningún otro identificador Passport) desde el request: el
// único parámetro del método es el id LOCAL de la ruta. Si algún día se
// agregara un parámetro adicional sin darse cuenta del riesgo, este test
// falla automáticamente.
public class BrebControllerEstadoMiRetiroTests
{
    [Fact]
    public void ActualizarEstadoMiRetiro_UnicoParametroEsIdLocalDeRuta_SinCuerpoDeRequest()
    {
        var method = typeof(BrebController).GetMethod(
            "ActualizarEstadoMiRetiro", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Método no encontrado — ¿fue renombrado?");

        var parameters = method.GetParameters();

        Assert.Single(parameters);
        Assert.Equal("id", parameters[0].Name);
        Assert.Equal(typeof(long), parameters[0].ParameterType);
        // Ningún atributo [FromBody] — no existe ningún parámetro de
        // cuerpo del request en absoluto, por lo tanto tampoco puede
        // existir uno de nombre payment_id/paymentId.
        Assert.DoesNotContain(parameters[0].GetCustomAttributes(), a => a.GetType().Name.Contains("FromBody"));
    }

    [Fact]
    public void ActualizarEstadoMiRetiro_RequiereAutorizacionYKycAprobado()
    {
        var method = typeof(BrebController).GetMethod(
            "ActualizarEstadoMiRetiro", BindingFlags.Public | BindingFlags.Instance)!;

        var authorizeAttrs = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .ToList();

        // [Authorize] simple (cualquier usuario autenticado) + [Authorize(Policy="KycAprobado")]
        Assert.Contains(authorizeAttrs, a => a.Policy is null);
        Assert.Contains(authorizeAttrs, a => a.Policy == "KycAprobado");

        // Nunca debe llevar Roles=ADMIN_XPAY/SUPERUSUARIO — ese es el
        // endpoint admin (distinto, ya existente) — este es user-side.
        Assert.DoesNotContain(authorizeAttrs, a => a.Roles is not null);
    }
}
