using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Reglas sin base de datos de la verificación del destinatario: nombre
// enmascarado, forma de la respuesta y hash de idempotencia de la
// transferencia (v2, con el destinatario confirmado).
public sealed class DestinatarioWalletTests
{
    [Theory]
    [InlineData("Gabriel", "Pineda", "Gabriel P.")]
    [InlineData("  María José ", "  ortiz", "María O.")]
    [InlineData("Ana", null, "Ana")]
    [InlineData("Ana", "   ", "Ana")]
    [InlineData(null, "Pineda", null)]
    [InlineData("   ", "Pineda", null)]
    public void EnmascararNombre_PrimerNombreEInicialDelApellido(string? nombre, string? apellido, string? esperado)
        => Assert.Equal(esperado, DestinatarioWalletService.EnmascararNombre(nombre, apellido));

    [Fact]
    public void Respuesta_SoloExponeWalletUsuarioYNombreVisible()
    {
        var propiedades = typeof(DestinatarioWalletResponse).GetProperties().Select(p => p.Name).OrderBy(n => n);
        Assert.Equal(new[] { "IdWallet", "NombreVisible", "Usuario" }, propiedades);
    }

    // Contrato del GET: misma exigencia que transferir y límite de consultas propio.
    [Fact]
    public void ObtenerDestinatario_ExigeKycAprobadoYLimiteDeConsultas()
    {
        var metodo = typeof(WalletsController).GetMethod(nameof(WalletsController.ObtenerDestinatario))!;
        Assert.Equal("KycAprobado", metodo.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("DestinatarioWalletPolicy", metodo.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal("destinatario/{idWallet:long}", metodo.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    [Fact]
    public void HashTransferencia_CambiaSiCambiaElDestinatarioEsperado()
    {
        var a = IdempotencyHashHelper.ComputeTransferenciaHash(1, 2, 3, 100m, "x", "usuario.a");
        var b = IdempotencyHashHelper.ComputeTransferenciaHash(1, 2, 3, 100m, "x", "usuario.b");
        var aConEspacios = IdempotencyHashHelper.ComputeTransferenciaHash(1, 2, 3, 100m, "x", "  usuario.a ");
        Assert.NotEqual(a, b);
        Assert.Equal(a, aConEspacios);
    }
}
