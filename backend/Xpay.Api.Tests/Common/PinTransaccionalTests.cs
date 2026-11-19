using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

// PIN transaccional — reglas de formato, filtro fail-closed (sin base) y
// arquitectura: los endpoints de dinero del usuario final lo exigen.
public sealed class PinTransaccionalTests
{
    [Theory]
    [InlineData("258013", true)]
    [InlineData("025801", true)]
    [InlineData("000000", true)]
    [InlineData("2580", false)]
    [InlineData("25801", false)]
    [InlineData("2580147", false)]
    [InlineData("25801a", false)]
    [InlineData("２５８０１３", false)] // dígitos no ASCII
    [InlineData("", false)]
    [InlineData(null, false)]
    public void FormatoValido(string? pin, bool esperado) =>
        Assert.Equal(esperado, PinTransaccionalService.FormatoValido(pin));

    [Theory]
    [InlineData("111111", true)]
    [InlineData("123456", true)]
    [InlineData("012345", true)]
    [InlineData("765432", true)]
    [InlineData("987654", true)]
    [InlineData("258013", false)]
    [InlineData("123457", false)]
    [InlineData("111112", false)]
    public void EsTrivial(string pin, bool esperado) =>
        Assert.Equal(esperado, PinTransaccionalService.EsTrivial(pin));

    [Theory]
    [InlineData("25801", "25801")]
    [InlineData("2580147", "2580147")]
    [InlineData("25801a", "25801a")]
    [InlineData("111111", "111111")]
    [InlineData("123456", "123456")]
    [InlineData("258013", "258014")]
    public async Task Crear_FormatoTrivialOConfirmacionInvalida_RechazaAntesDeConsultarLaBase(string pin, string confirmacion)
    {
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=NoExiste;Connect Timeout=1;").Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PinTransaccionalService(db)
            .CrearAsync(42, new CrearPinRequest { ClaveActual = "NoSeConsulta", Pin = pin, ConfirmacionPin = confirmacion }));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("25801", "25801")]
    [InlineData("2580147", "2580147")]
    [InlineData("111111", "111111")]
    [InlineData("654321", "654321")]
    [InlineData("258013", "258014")]
    public async Task Cambiar_PinNuevoInvalido_RechazaAntesDeProbarElPinActual(string nuevo, string confirmacion)
    {
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=NoExiste;Connect Timeout=1;").Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PinTransaccionalService(db)
            .CambiarAsync(42, new CambiarPinRequest { PinActual = "8520963", PinNuevo = nuevo, ConfirmacionPinNuevo = confirmacion }));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    // ── Filtro ───────────────────────────────────────────────────────────

    private sealed class Cuerpo : IConPinTransaccional { public string? Pin { get; set; } }

    private static (ActionExecutingContext Ctx, Cuerpo? Body) Contexto(bool conServicio, bool conClaim, bool conCuerpo, string? pin)
    {
        var services = new ServiceCollection();
        if (conServicio)
        {
            // Contexto sin servidor: estas pruebas nunca llegan a la base.
            services.AddSingleton(new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>()
                .UseSqlServer("Server=127.0.0.1,1;Database=NoExiste;Connect Timeout=1;").Options));
            services.AddScoped<PinTransaccionalService>();
        }
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (conClaim) http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("idUsuario", "42") }, "test"));

        var body = conCuerpo ? new Cuerpo { Pin = pin } : null;
        var args = new Dictionary<string, object?>();
        if (body is not null) args["request"] = body;
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return (new ActionExecutingContext(action, new List<IFilterMetadata>(), args, controller: new object()), body);
    }

    private static async Task<bool> EjecutarAsync(ActionExecutingContext ctx)
    {
        var llamado = false;
        await new RequierePinTransaccionalAttribute().OnActionExecutionAsync(ctx, () =>
        {
            llamado = true;
            return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), new object()));
        });
        return llamado;
    }

    [Fact]
    public async Task SinServicio_FallaCerrado_500()
    {
        var (ctx, _) = Contexto(conServicio: false, conClaim: true, conCuerpo: true, pin: "258013");
        Assert.False(await EjecutarAsync(ctx));
        Assert.Equal(500, Assert.IsType<ObjectResult>(ctx.Result).StatusCode);
    }

    [Fact]
    public async Task SinCuerpoConPin_FallaCerrado_500()
    {
        var (ctx, _) = Contexto(conServicio: true, conClaim: true, conCuerpo: false, pin: null);
        Assert.False(await EjecutarAsync(ctx));
        Assert.Equal(500, Assert.IsType<ObjectResult>(ctx.Result).StatusCode);
    }

    [Fact]
    public async Task SinIdUsuario_401()
    {
        var (ctx, _) = Contexto(conServicio: true, conClaim: false, conCuerpo: true, pin: "258013");
        Assert.False(await EjecutarAsync(ctx));
        Assert.IsType<UnauthorizedObjectResult>(ctx.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("25801478")]
    [InlineData("abcdef")]
    public async Task PinAusenteOMalFormado_400_PinRequerido_YLoBorra(string? pin)
    {
        var (ctx, body) = Contexto(conServicio: true, conClaim: true, conCuerpo: true, pin: pin);
        Assert.False(await EjecutarAsync(ctx));
        var r = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(400, r.StatusCode);
        Assert.Contains("PIN_REQUERIDO", System.Text.Json.JsonSerializer.Serialize(r.Value));
        Assert.Null(body!.Pin);
    }

    // ── Arquitectura ─────────────────────────────────────────────────────

    private static readonly string[] EndpointsConPin =
    [
        "BrebController.SolicitarRetiroReal",
        "LibranzaClienteController.SolicitarAnticipo",
        "QrController.Pagar",
        "WalletsController.Transferir",
    ];

    [Fact]
    public void EndpointsDeDineroDelUsuario_ExigenPin_ConCuerpoIConPin_YOperacionFinanciera()
    {
        var marcados = typeof(RequierePinTransaccionalAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<RequierePinTransaccionalAttribute>() is not null)
            .ToList();

        Assert.Equal(EndpointsConPin, marcados.Select(m => $"{m.DeclaringType!.Name}.{m.Name}").OrderBy(n => n, StringComparer.Ordinal));
        foreach (var m in marcados)
        {
            Assert.NotNull(m.GetCustomAttribute<OperacionFinancieraAttribute>());
            Assert.Single(m.GetParameters(), p => typeof(IConPinTransaccional).IsAssignableFrom(p.ParameterType));
        }
    }
}
