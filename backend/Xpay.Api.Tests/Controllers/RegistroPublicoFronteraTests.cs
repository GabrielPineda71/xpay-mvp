using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// P0-B1 — frontera de los endpoints de registro: los legacy (registro-final,
// registro-inicial) y los nuevos (registro, verificar-email) son anónimos pero
// solo existen en Development/QA/Test; en productivo el filtro responde 404.
public class RegistroPublicoFronteraTests
{
    public static TheoryData<Type, string> AccionesDeRegistro => new()
    {
        { typeof(UsuariosController),        nameof(UsuariosController.RegistrarUsuarioFinal) },
        { typeof(UsuariosController),        nameof(UsuariosController.RegistrarInicial) },
        { typeof(RegistroPublicoController), nameof(RegistroPublicoController.Registrar) },
        { typeof(RegistroPublicoController), nameof(RegistroPublicoController.VerificarEmail) },
        { typeof(RegistroPublicoController), nameof(RegistroPublicoController.ReenviarVerificacion) },
    };

    private static MethodInfo Accion(Type controller, string metodo)
        => controller.GetMethod(metodo) ?? throw new InvalidOperationException($"No existe {controller.Name}.{metodo}");

    [Theory]
    [MemberData(nameof(AccionesDeRegistro))]
    public void AccionDeRegistro_EsAnonima_YSoloNoProductiva(Type controller, string metodo)
    {
        var accion = Accion(controller, metodo);
        Assert.True(accion.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.True(accion.IsDefined(typeof(SoloAmbienteNoProductivoAttribute), inherit: true));
    }

    [Theory]
    [InlineData(nameof(RegistroPublicoController.Registrar))]
    [InlineData(nameof(RegistroPublicoController.VerificarEmail))]
    [InlineData(nameof(RegistroPublicoController.ReenviarVerificacion))]
    public void EndpointsNuevos_UsanRateLimitingDeIdentidad(string metodo)
        => Assert.Equal("IdentidadPublicaPolicy",
            Accion(typeof(RegistroPublicoController), metodo).GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);

    [Fact]
    public void VerificarEmail_RecibeElTokenEnElBody_NoEnLaQuery()
    {
        var parametro = Assert.Single(
            Accion(typeof(RegistroPublicoController), nameof(RegistroPublicoController.VerificarEmail)).GetParameters(),
            p => p.ParameterType != typeof(CancellationToken));
        Assert.True(parametro.IsDefined(typeof(FromBodyAttribute), inherit: false));
        Assert.False(parametro.IsDefined(typeof(FromQueryAttribute), inherit: false));
    }

    [Fact]
    public void ReenviarVerificacion_RecibeSoloElUsuarioEnElBody()
    {
        var parametro = Assert.Single(
            Accion(typeof(RegistroPublicoController), nameof(RegistroPublicoController.ReenviarVerificacion)).GetParameters(),
            p => p.ParameterType != typeof(CancellationToken));
        Assert.True(parametro.IsDefined(typeof(FromBodyAttribute), inherit: false));
        Assert.False(parametro.IsDefined(typeof(FromQueryAttribute), inherit: false));
        Assert.Equal(new[] { "Usuario" }, typeof(ReenviarVerificacionRequest).GetProperties().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void ControllersDeRegistro_NoLlevanFronteraDeClase()
    {
        Assert.False(typeof(RegistroPublicoController).IsDefined(typeof(SoloAmbienteNoProductivoAttribute), inherit: true));
        Assert.False(typeof(UsuariosController).IsDefined(typeof(SoloAmbienteNoProductivoAttribute), inherit: true));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("qa")]
    [InlineData("")]
    public void Frontera_EnProductivo_Responde404(string ambiente)
        => Assert.IsType<NotFoundObjectResult>(EjecutarFrontera(ambiente));

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void Frontera_EnNoProductivo_DejaPasar_CompatibilidadQaCi(string ambiente)
        => Assert.Null(EjecutarFrontera(ambiente));

    private static IActionResult? EjecutarFrontera(string ambiente)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(AmbienteXpayTests.Ambiente(ambiente))
                .BuildServiceProvider(),
        };
        var contexto = new ResourceExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());
        new SoloAmbienteNoProductivoAttribute().OnResourceExecuting(contexto);
        return contexto.Result;
    }
}
