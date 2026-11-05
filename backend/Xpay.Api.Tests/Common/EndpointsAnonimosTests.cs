using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xunit;

namespace Xpay.Api.Tests.Common;

// P0-A — con FallbackPolicy (deny-by-default) los únicos endpoints públicos son
// los marcados [AllowAnonymous]. Este test fija el conjunto CERRADO: falla si
// falta uno o si aparece cualquier [AllowAnonymous] adicional.
public class EndpointsAnonimosTests
{
    private static readonly string[] Esperados =
    {
        "GET api/diagnostics/error-test",
        "GET api/diagnostics/ping",
        "GET api/diagnostics/ready",
        "GET api/version",
        "GET health",
        "POST api/auth/login",
        "POST api/auth/recuperar-clave",
        "POST api/auth/restablecer-clave",
        "POST api/kyc/veriff/webhook",
        "POST api/passport/webhook",
        "POST api/usuarios/registro",
        "POST api/usuarios/registro-final",
        "POST api/usuarios/registro-inicial",
        "POST api/usuarios/reenviar-verificacion",
        "POST api/usuarios/verificar-email",
    };

    private static IEnumerable<(Type Controller, MethodInfo Accion, string Endpoint)> Acciones()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        foreach (var c in controllers)
        {
            var rutaClase = c.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                foreach (var h in m.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var plantilla = h.Template ?? string.Empty;
                    var ruta = plantilla.StartsWith('/') ? plantilla
                             : string.IsNullOrEmpty(plantilla) ? rutaClase
                             : string.IsNullOrEmpty(rutaClase) ? plantilla
                             : $"{rutaClase}/{plantilla}";
                    yield return (c, m, $"{h.HttpMethods.Single()} {ruta.TrimStart('/')}");
                }
        }
    }

    private static bool EsAnonima(Type c, MethodInfo m) =>
        m.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
        || c.IsDefined(typeof(AllowAnonymousAttribute), inherit: true);

    [Fact]
    public void AccionesAnonimas_SonExactamenteLasAcordadas()
    {
        var anonimas = Acciones().Where(a => EsAnonima(a.Controller, a.Accion))
            .Select(a => a.Endpoint).OrderBy(e => e, StringComparer.Ordinal).ToList();

        Assert.Equal(Esperados.OrderBy(e => e, StringComparer.Ordinal), anonimas);
    }

    [Fact]
    public void NingunController_DeclaraAllowAnonymousAnivelDeClase()
    {
        var conAnonimoDeClase = typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.IsDefined(typeof(AllowAnonymousAttribute), true))
            .Select(t => t.Name).ToList();

        Assert.Empty(conAnonimoDeClase);
    }

    [Fact]
    public void ErrorTest_ConservaRestriccionNoProductiva()
    {
        var accion = Acciones().Single(a => a.Endpoint == "GET api/diagnostics/error-test").Accion;
        Assert.True(accion.IsDefined(typeof(SoloAmbienteNoProductivoAttribute), inherit: true));
    }
}
