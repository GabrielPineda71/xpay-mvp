using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// XPAY-712 - A: ConfirmarAvanceWallet rechaza con 400 una
// Idempotency-Key ausente, invalida o duplicada ANTES de llamar
// al servicio. Dependencias en null: si el servicio se invocara,
// el test fallaria con NullReferenceException. Sin red ni BD.
public sealed class CarteraOrdinariaControllerIdempotenciaTests
{
    private static readonly SimularUtilizacionRequest Req =
        new("AVANCE_WALLET", 100000m, 1, "MENSUAL");

    private static CarteraOrdinariaController Controller(StringValues? key)
    {
        var http = new DefaultHttpContext();
        if (key.HasValue)
            http.Request.Headers["Idempotency-Key"] = key.Value;

        var c = new CarteraOrdinariaController(
            null!, null!, null!, null!, null!, null!, null!);
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static string? Error(IActionResult r)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(r);
        return bad.Value!.GetType().GetProperty("error")!
            .GetValue(bad.Value) as string;
    }

    [Fact]
    public async Task Sin_header_400()
    {
        var r = await Controller(null).ConfirmarAvanceWallet(Req);
        Assert.Equal("Falta el encabezado Idempotency-Key.", Error(r));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Key_invalida_400(string valor)
    {
        var r = await Controller(new StringValues(valor))
            .ConfirmarAvanceWallet(Req);
        Assert.Equal(
            "Idempotency-Key debe ser un identificador válido.",
            Error(r));
    }

    [Fact]
    public async Task Key_duplicada_400()
    {
        var dos = new StringValues(new[]
        {
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
        });
        var r = await Controller(dos).ConfirmarAvanceWallet(Req);
        Assert.Equal(
            "Se recibió más de un valor para Idempotency-Key.",
            Error(r));
    }
}
