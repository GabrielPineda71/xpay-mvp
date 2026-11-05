using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// P0-B2 — frontera de recuperación de contraseña: anónima, solo no
// productiva (el comportamiento 404 del filtro ya lo cubre
// RegistroPublicoFronteraTests), rate limiting de identidad y datos
// sensibles solo en el body.
public class RecuperacionClaveFronteraTests
{
    private static MethodInfo Accion(string metodo)
        => typeof(RecuperacionClaveController).GetMethod(metodo)
           ?? throw new InvalidOperationException($"No existe RecuperacionClaveController.{metodo}");

    [Theory]
    [InlineData(nameof(RecuperacionClaveController.SolicitarRecuperacion))]
    [InlineData(nameof(RecuperacionClaveController.RestablecerClave))]
    public void Accion_EsAnonima_SoloNoProductiva_YConRateLimitingDeIdentidad(string metodo)
    {
        var accion = Accion(metodo);
        Assert.True(accion.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.True(accion.IsDefined(typeof(SoloAmbienteNoProductivoAttribute), inherit: true));
        Assert.Equal("IdentidadPublicaPolicy", accion.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Theory]
    [InlineData(nameof(RecuperacionClaveController.SolicitarRecuperacion))]
    [InlineData(nameof(RecuperacionClaveController.RestablecerClave))]
    public void Accion_RecibeLosDatosEnElBody_NoEnLaQuery(string metodo)
    {
        var parametro = Assert.Single(Accion(metodo).GetParameters(), p => p.ParameterType != typeof(CancellationToken));
        Assert.True(parametro.IsDefined(typeof(FromBodyAttribute), inherit: false));
        Assert.False(parametro.IsDefined(typeof(FromQueryAttribute), inherit: false));
    }

    [Fact]
    public void Controller_NoLlevaFronteraNiAnonimoDeClase()
    {
        Assert.False(typeof(RecuperacionClaveController).IsDefined(typeof(SoloAmbienteNoProductivoAttribute), inherit: true));
        Assert.False(typeof(RecuperacionClaveController).IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public void SolicitudPublica_SoloAceptaNombreDeUsuario()
        => Assert.Equal(new[] { "Usuario" },
            typeof(SolicitarRecuperacionClaveRequest).GetProperties().Select(p => p.Name).ToArray());

    [Fact]
    public void RestablecerRequest_ToString_NoRevelaTokenNiClaves()
    {
        var texto = new RestablecerClaveRecuperacionRequest("TOKEN-SECRETO", "Clave#Nueva1", "Clave#Nueva1").ToString();
        Assert.DoesNotContain("TOKEN-SECRETO", texto);
        Assert.DoesNotContain("Clave#Nueva1", texto);
    }
}
