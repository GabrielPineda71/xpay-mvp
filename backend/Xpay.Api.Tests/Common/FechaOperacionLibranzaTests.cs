using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xunit;

namespace Xpay.Api.Tests.Common;

// XPAY-717 — fecha simulada de Libranza subordinada a la frontera central
// IAmbienteXpay. Sin DB ni red. Los ambientes se construyen con el AmbienteXpay
// real (AmbienteXpayTests.Ambiente). Los tests de controller usan el servicio
// en null: si la acción llegara a invocarlo, el NullReferenceException cae en
// su catch genérico y la respuesta es 500, nunca el 400 esperado.
public sealed class FechaOperacionLibranzaTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 12);

    // ── Helper: productivo ─────────────────────────────────────────────────
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("qa")]
    [InlineData("Qa")]
    [InlineData("QA ")]
    [InlineData("development")]
    [InlineData("TEST")]
    [InlineData("Prod")]
    [InlineData("Desconocido")]
    public void Productivo_con_fecha_valida_se_rechaza(string nombre)
    {
        var r = FechaOperacionLibranza.Resolver(AmbienteXpayTests.Ambiente(nombre), "2026-01-15", Hoy, out var fecha);

        Assert.Equal(ResultadoFechaLibranza.RechazadaAmbienteProductivo, r);
        Assert.Equal(Hoy, fecha);
    }

    // Presente pero vacía/espacios/basura: se rechaza por estar presente, sin parsear.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("basura")]
    [InlineData("2026-13-45")]
    public void Productivo_con_fecha_presente_no_valida_se_rechaza_sin_parsear(string valor)
    {
        var r = FechaOperacionLibranza.Resolver(AmbienteXpayTests.Ambiente("Production"), valor, Hoy, out var fecha);

        Assert.Equal(ResultadoFechaLibranza.RechazadaAmbienteProductivo, r);
        Assert.Equal(Hoy, fecha);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Productivo_sin_fecha_usa_hoy(string nombre)
    {
        var r = FechaOperacionLibranza.Resolver(AmbienteXpayTests.Ambiente(nombre), null, Hoy, out var fecha);

        Assert.Equal(ResultadoFechaLibranza.NoSuministrada, r);
        Assert.Equal(Hoy, fecha);
    }

    // ── Helper: no productivo ──────────────────────────────────────────────
    private static readonly string[] NoProductivos = { "Development", "QA", "Test" };

    // Esperado como año/mes/día (tipos serializables por xunit).
    public static TheoryData<string, string, int, int, int> NoProductivoFechasValidas()
    {
        var data = new TheoryData<string, string, int, int, int>();
        foreach (var nombre in NoProductivos)
        {
            data.Add(nombre, "2026-01-15", 2026, 1, 15);
            data.Add(nombre, "2020-02-29", 2020, 2, 29);  // pasada: sin regla de rango
            data.Add(nombre, "2031-12-31", 2031, 12, 31); // futura: sin regla de rango
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(NoProductivoFechasValidas))]
    public void No_productivo_con_fecha_valida_es_simulada(string nombre, string valor, int anio, int mes, int dia)
    {
        var r = FechaOperacionLibranza.Resolver(AmbienteXpayTests.Ambiente(nombre), valor, Hoy, out var fecha);

        Assert.Equal(ResultadoFechaLibranza.Simulada, r);
        Assert.Equal(new DateOnly(anio, mes, dia), fecha);
    }

    public static TheoryData<string, string> NoProductivoFechasInvalidas()
    {
        var data = new TheoryData<string, string>();
        foreach (var nombre in NoProductivos)
            foreach (var valor in new[] { "no-es-fecha", "", " ", "2026-13-45" })
                data.Add(nombre, valor);
        return data;
    }

    [Theory]
    [MemberData(nameof(NoProductivoFechasInvalidas))]
    public void No_productivo_con_formato_invalido(string nombre, string valor)
    {
        var r = FechaOperacionLibranza.Resolver(AmbienteXpayTests.Ambiente(nombre), valor, Hoy, out var fecha);

        Assert.Equal(ResultadoFechaLibranza.FormatoInvalido, r);
        Assert.Equal(Hoy, fecha);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void No_productivo_sin_fecha_usa_hoy(string nombre)
    {
        var r = FechaOperacionLibranza.Resolver(AmbienteXpayTests.Ambiente(nombre), null, Hoy, out var fecha);

        Assert.Equal(ResultadoFechaLibranza.NoSuministrada, r);
        Assert.Equal(Hoy, fecha);
    }

    // ── Helper: frontera, argumentos y estructura ──────────────────────────
    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("")]
    public void Frontera_productiva_prevalece_sobre_el_nombre(string valor)
    {
        var ambiente = new AmbienteFalso(AmbienteXpay.Development, permiteNoProductivos: false);

        Assert.Equal(ResultadoFechaLibranza.RechazadaAmbienteProductivo,
            FechaOperacionLibranza.Resolver(ambiente, valor, Hoy, out _));
    }

    [Fact]
    public void Ambiente_nulo_lanza_ArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => FechaOperacionLibranza.Resolver(null!, "2026-01-15", Hoy, out _));

    [Fact]
    public void Helper_no_depende_de_configuracion_ni_host()
    {
        var tipo = typeof(FechaOperacionLibranza);
        Assert.True(tipo.IsAbstract && tipo.IsSealed); // clase estática

        var tipos = tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType)
            .Concat(tipo.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Select(f => f.FieldType))
            .ToList();

        Assert.DoesNotContain(tipos, t => typeof(IConfiguration).IsAssignableFrom(t));
        Assert.DoesNotContain(tipos, t => typeof(IHostEnvironment).IsAssignableFrom(t));
    }

    // ── Controller: el rechazo ocurre ANTES del servicio ───────────────────
    private static LibranzaClienteController Controller(IAmbienteXpay ambiente)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("idUsuario", "10") }, "test")),
        };
        var c = new LibranzaClienteController(
            null!, NullLogger<LibranzaClienteController>.Instance, ambiente);
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static string? Mensaje400(IActionResult r)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(r);
        return bad.Value!.GetType().GetProperty("message")!.GetValue(bad.Value) as string;
    }

    // Llegó al servicio (null) → NullReferenceException capturada → 500.
    private static void LlegoAlServicio(IActionResult r)
        => Assert.Equal(500, Assert.IsType<ObjectResult>(r).StatusCode);

    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("")]
    [InlineData("basura")]
    public async Task Produccion_GetMiCupo_con_fecha_responde_400_antes_del_servicio(string fecha)
    {
        var r = await Controller(AmbienteXpayTests.Ambiente("Production")).GetMiCupo(fecha);

        Assert.Equal(LibranzaClienteController.MensajeFechaSimuladaNoPermitida, Mensaje400(r));
    }

    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("")]
    [InlineData("basura")]
    public async Task Produccion_SolicitarAnticipo_con_fecha_responde_400_antes_del_servicio(string fecha)
    {
        var req = new SolicitarAnticipoRequest { ValorSolicitado = 1000m, FechaSimulada = fecha };

        var r = await Controller(AmbienteXpayTests.Ambiente("Production")).SolicitarAnticipo(req);

        Assert.Equal(LibranzaClienteController.MensajeFechaSimuladaNoPermitida, Mensaje400(r));
    }

    [Fact]
    public async Task No_productivo_GetMiCupo_formato_invalido_responde_400_antes_del_servicio()
    {
        var r = await Controller(AmbienteXpayTests.Ambiente("QA")).GetMiCupo("no-es-fecha");

        Assert.Equal(LibranzaClienteController.MensajeFormatoFechaInvalido, Mensaje400(r));
    }

    [Fact]
    public async Task No_productivo_SolicitarAnticipo_formato_invalido_responde_400_antes_del_servicio()
    {
        var req = new SolicitarAnticipoRequest { ValorSolicitado = 1000m, FechaSimulada = "no-es-fecha" };

        var r = await Controller(AmbienteXpayTests.Ambiente("QA")).SolicitarAnticipo(req);

        Assert.Equal(LibranzaClienteController.MensajeFormatoFechaInvalido, Mensaje400(r));
    }

    // Controles: sin fecha (o simulada válida en no productivo) el flujo
    // continúa hasta el servicio. Prueban que el 400 anterior no es casual.
    [Fact]
    public async Task Produccion_GetMiCupo_sin_fecha_continua_al_servicio()
        => LlegoAlServicio(await Controller(AmbienteXpayTests.Ambiente("Production")).GetMiCupo(null));

    [Fact]
    public async Task Produccion_SolicitarAnticipo_sin_fecha_continua_al_servicio_con_null()
    {
        var req = new SolicitarAnticipoRequest { ValorSolicitado = 1000m, FechaSimulada = null };

        LlegoAlServicio(await Controller(AmbienteXpayTests.Ambiente("Production")).SolicitarAnticipo(req));
        Assert.Null(req.FechaSimulada);
    }

    [Fact]
    public async Task No_productivo_SolicitarAnticipo_fecha_valida_continua_al_servicio_en_ISO()
    {
        var req = new SolicitarAnticipoRequest { ValorSolicitado = 1000m, FechaSimulada = "2026-01-15" };

        LlegoAlServicio(await Controller(AmbienteXpayTests.Ambiente("QA")).SolicitarAnticipo(req));
        Assert.Equal("2026-01-15", req.FechaSimulada);
    }

    // ── Controller: estructura ─────────────────────────────────────────────
    [Fact]
    public void Constructor_recibe_IAmbienteXpay()
    {
        var parametros = typeof(LibranzaClienteController).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        Assert.Contains(typeof(IAmbienteXpay), parametros);
    }

    [Theory]
    [InlineData(nameof(LibranzaClienteController.GetMiCupo))]
    [InlineData(nameof(LibranzaClienteController.SolicitarAnticipo))]
    [InlineData(nameof(LibranzaClienteController.GetMisAnticipos))]
    public void Acciones_conservan_KycAprobado(string metodo)
    {
        var accion = Assert.Single(
            typeof(LibranzaClienteController).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            m => m.Name == metodo);

        Assert.Contains(accion.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "KycAprobado");
    }

    private sealed class AmbienteFalso(string nombre, bool permiteNoProductivos) : IAmbienteXpay
    {
        public string Nombre => nombre;
        public bool PermiteMecanismosNoProductivos => permiteNoProductivos;
        public bool EsProductivo => !permiteNoProductivos;
    }
}
