using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xunit;

namespace Xpay.Api.Tests.Common;

public sealed class CuentaLimitadaFilterTests
{
    // Solo estas acciones han sido revisadas para el perfil limitado. Una acción
    // nueva, aun dentro de los mismos controladores, no hereda la excepción.
    [Fact]
    public void Excepciones_solo_en_acciones_revisadas_de_perfil_seguridad_contactos_y_estado_kyc()
    {
        var esperadas = new HashSet<string>
        {
            "UsuariosController.ObtenerMiPerfil", "UsuariosController.ActualizarMiPerfil",
            "AuthController.CambiarClaveObligatoria", "AuthController.CambiarClave",
            "ContactosController.Estado", "ContactosController.SolicitarCodigo",
            "ContactosController.Verificar", "ContactosController.Preferencias",
            "KycController.MiEstado", "KycController.VeriffSession",
            "ConsentimientoBiometricoController.Estado", "ConsentimientoBiometricoController.Autorizar",
        };
        var acciones = typeof(UsuariosController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.IsDefined(typeof(CuentaLimitadaPermitidaAttribute), true))
                .Select(m => (Tipo: t, Metodo: m)))
            .ToList();

        Assert.True(esperadas.SetEquals(acciones.Select(a => a.Tipo.Name + "." + a.Metodo.Name)));
        Assert.All(acciones, a =>
        {
            Assert.False(a.Metodo.IsDefined(typeof(OperacionFinancieraAttribute), true));
            Assert.False(a.Metodo.IsDefined(typeof(AllowAnonymousAttribute), true));
        });
        Assert.Equal(AttributeTargets.Method,
            typeof(CuentaLimitadaPermitidaAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.ValidOn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("usuario-invalido")]
    [InlineData(" 123 ")]
    [InlineData("+123")]
    [InlineData("0123")]
    [InlineData("9223372036854775808")]
    public async Task Usuario_autenticado_sin_id_valido_no_ejecuta_ni_consulta_sql(string? id)
    {
        await using var db = SinProveedor();
        var contexto = Contexto(typeof(UsuariosController), nameof(UsuariosController.ObtenerMiPerfil), id);
        var ejecutada = false;

        await Filtro(db).OnResourceExecutionAsync(contexto, () =>
        {
            ejecutada = true;
            return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters));
        });

        Assert.False(ejecutada);
        Assert.IsType<UnauthorizedObjectResult>(contexto.Result);
    }

    [Fact]
    public async Task Fallo_de_consulta_bloquea_incluso_accion_permitida_y_no_se_interpreta_como_piloto()
    {
        await using var db = SinProveedor(); // AnyAsync falla sin abrir red.
        var contexto = Contexto(typeof(UsuariosController), nameof(UsuariosController.ObtenerMiPerfil), "123");
        var ejecutada = false;
        await Filtro(db).OnResourceExecutionAsync(contexto, () =>
        {
            ejecutada = true;
            return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters));
        });

        Assert.False(ejecutada);
        Assert.Equal(503, Assert.IsType<ObjectResult>(contexto.Result).StatusCode);
    }

    [Fact]
    public async Task Identidad_con_id_duplicado_falla_cerrada()
    {
        await using var db = SinProveedor();
        var contexto = Contexto(typeof(UsuariosController), nameof(UsuariosController.ObtenerMiPerfil), "123");
        ((ClaimsIdentity)contexto.HttpContext.User.Identity!).AddClaim(new Claim("idUsuario", "456"));
        var ejecutada = false;
        await Filtro(db).OnResourceExecutionAsync(contexto, () =>
        {
            ejecutada = true;
            return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters));
        });
        Assert.False(ejecutada);
        Assert.IsType<UnauthorizedObjectResult>(contexto.Result);
    }

    [Fact]
    public async Task Accion_anonima_con_bearer_no_depende_de_los_permisos_de_esa_cuenta()
    {
        await using var db = SinProveedor();
        var contexto = Contexto(typeof(AuthController), nameof(AuthController.Login), null);
        var ejecutada = false;
        await Filtro(db).OnResourceExecutionAsync(contexto, () =>
        {
            ejecutada = true;
            return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters));
        });

        Assert.True(ejecutada);
        Assert.Null(contexto.Result);
    }

    private static XpayDbContext SinProveedor() => new(new DbContextOptionsBuilder<XpayDbContext>().Options);
    private static CuentaLimitadaFilter Filtro(XpayDbContext db) =>
        new(db, NullLogger<CuentaLimitadaFilter>.Instance);

    private static ResourceExecutingContext Contexto(Type controller, string metodo, string? id)
    {
        var claims = id is null ? Array.Empty<Claim>() : new[] { new Claim("idUsuario", id) };
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba-autenticada")),
        };
        var accion = new ControllerActionDescriptor
        {
            ControllerTypeInfo = controller.GetTypeInfo(),
            MethodInfo = controller.GetMethod(metodo)!,
        };
        return new ResourceExecutingContext(new ActionContext(http, new RouteData(), accion),
            new List<IFilterMetadata>(), new List<IValueProviderFactory>());
    }
}
