using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.Services;
using Xpay.Api.Tests.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

[Collection("SqlIntegration")]
public sealed class AltaWalletSinCreditoTests
{
    [Theory]
    [InlineData(typeof(CarteraOrdinariaController), "true", "SUPERUSUARIO")]
    [InlineData(typeof(CarteraOrdinariaController), "false", "CARTERA_XPAY")]
    [InlineData(typeof(LibranzaClienteController), "true", "USUARIO_FINAL")]
    [InlineData(typeof(LibranzaClienteController), "false", "SUPERUSUARIO")]
    public async Task AutorregistroConWalletRealActiva_NoObtieneCreditoPorFlagsRolesNiClaims(Type controlador, string flag, string rolClaim)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new PreparacionWalletSqlTests.Semillero(cs);
        var config = PreparacionWalletSqlTests.Configuracion();
        var cuenta = await datos.ListaAsync(config);
        await using var db = Contexto(cs);
        var login = await PreparacionWalletSqlTests.Servicio(db, config).ActivarAsync(cuenta.Usuario,
            new(true, CondicionesWalletPiloto.Version), null);
        Assert.False(login.RequierePerfil);
        Assert.False(await RegistroPublicoService.EsCuentaLimitadaAsync(db, cuenta.Usuario));
        Assert.True(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona && w.Estado == "ACTIVA"));
        config[CondicionesWalletPiloto.ClaveHabilitada] = flag;
        var contexto = ContextoFiltro(db, config, cuenta.Usuario, controlador, rolClaim);
        var ejecutoAccion = false;
        await Filtro(controlador).OnResourceExecutionAsync(contexto, () =>
        {
            ejecutoAccion = true;
            return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters));
        });
        Assert.False(ejecutoAccion);
        var respuesta = Assert.IsType<ObjectResult>(contexto.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, respuesta.StatusCode);
        var json = JsonSerializer.SerializeToElement(respuesta.Value);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("CREDITO_REQUIERE_HABILITACION", json.GetProperty("codigo").GetString());
        Assert.False(await db.CarteraCuposOrdinarios.AnyAsync(c => c.IdUsuario == cuenta.Usuario));
        Assert.False(await db.CarteraSolicitudesCupo.AnyAsync(c => c.IdUsuario == cuenta.Usuario));
        Assert.False(await db.LibranzaAnticipos.AnyAsync(c => c.IdUsuario == cuenta.Usuario));
        Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(c => c.IdUsuario == cuenta.Usuario));
    }

    [Theory]
    [InlineData(typeof(CarteraOrdinariaController))]
    [InlineData(typeof(LibranzaClienteController))]
    public async Task PilotoAdministrativoPreexistente_ConservaSuFlujoCrediticioSeparado(Type controlador)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new PreparacionWalletSqlTests.Semillero(cs);
        var config = PreparacionWalletSqlTests.Configuracion();
        var admin = await datos.CuentaAsync(publica: false, rol: "SUPERUSUARIO");
        var solicitud = datos.NuevaSolicitudManual();
        await using var db = Contexto(cs);
        var alta = await new AltaUsuarioPilotoService(db, config, AmbienteXpayTests.Ambiente("Production"))
            .CrearAsync(solicitud, admin.Usuario);
        Assert.False(await RegistroPublicoService.EsAutorregistroAsync(db, alta.IdUsuario));
        Assert.True(await db.Wallets.AnyAsync(w => w.IdWallet == alta.IdWallet));
        var contexto = ContextoFiltro(db, config, alta.IdUsuario, controlador, "USUARIO_FINAL");
        var ejecutoAccion = false;
        await Filtro(controlador).OnResourceExecutionAsync(contexto, () =>
        {
            ejecutoAccion = true;
            return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters));
        });
        Assert.True(ejecutoAccion);
        Assert.Null(contexto.Result);
    }

    [Theory]
    [InlineData(typeof(CarteraOrdinariaController))]
    [InlineData(typeof(LibranzaClienteController))]
    public async Task FallaSql_NoSeInterpretaComoPilotoNiEjecutaLaOperacion(Type controlador)
    {
        if (!Conexion(out var cs)) return;
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>()
            .UseSqlServer(cs).AddInterceptors(new FallarLecturaSql()).Options);
        var config = PreparacionWalletSqlTests.Configuracion();
        var contexto = ContextoFiltro(db, config, 1, controlador, "SUPERUSUARIO");
        var ejecutoAccion = false;
        await Assert.ThrowsAsync<FalloSqlControlado>(() => Filtro(controlador).OnResourceExecutionAsync(contexto, () =>
        {
            ejecutoAccion = true;
            return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters));
        }));
        Assert.False(ejecutoAccion);
        Assert.Null(contexto.Result);
    }

    private static AltaWalletSinCreditoAttribute Filtro(Type controlador)
    {
        Assert.NotNull(controlador.GetCustomAttribute<AuthorizeAttribute>(inherit: true));
        return Assert.IsType<AltaWalletSinCreditoAttribute>(controlador.GetCustomAttribute<AltaWalletSinCreditoAttribute>(inherit: true));
    }

    private static ResourceExecutingContext ContextoFiltro(XpayDbContext db, IConfiguration config, long idUsuario, Type controlador, string rol)
    {
        var servicios = new ServiceCollection().AddSingleton(db).AddSingleton(config).BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = servicios,
            User = new(new ClaimsIdentity([
                new Claim("idUsuario", idUsuario.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim("requierePerfil", "false"), new Claim("walletHabilitada", "true"),
                new Claim("esRegistroPublico", "false"), new Claim(ClaimTypes.Role, rol), new Claim("vs", "999")
            ], "test"))
        };
        return new(new ActionContext(http, new RouteData(), new ControllerActionDescriptor
            { ControllerTypeInfo = controlador.GetTypeInfo() }), new List<IFilterMetadata>(), new List<IValueProviderFactory>());
    }

    private sealed class FalloSqlControlado : DbException;
    private sealed class FallarLecturaSql : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            => throw new FalloSqlControlado();
    }

    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
    private static bool Conexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? "";
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                     !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            "Se requiere conexión SQL de pruebas para verificar la separación de crédito del autorregistro.");
        return false;
    }
}
