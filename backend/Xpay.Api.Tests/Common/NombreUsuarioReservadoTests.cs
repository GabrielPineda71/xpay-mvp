using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

// XPAY-717 — nombres de usuario "qa.*" reservados en ambiente productivo,
// subordinados a la frontera central IAmbienteXpay. Sin DB ni red: los
// ambientes se construyen con el AmbienteXpay real (AmbienteXpayTests.Ambiente)
// y los servicios con XpayDbContext en null. Si un servicio llegara a la BD,
// el acceso a db.Database lanza NullReferenceException: así se distingue el
// rechazo de la guarda (InvalidOperationException) de "continuó al flujo".
public sealed class NombreUsuarioReservadoTests
{
    // ── Helper: qué es reservado ───────────────────────────────────────────
    [Theory]
    [InlineData("qa.x")]
    [InlineData("QA.x")]
    [InlineData("Qa.X")]
    [InlineData("qA.x")]
    [InlineData("  qa.x  ")]
    [InlineData("\tqa.x\n")]
    [InlineData(" qa.x")]
    [InlineData("qa.")]
    [InlineData("QA.")]
    [InlineData("qa.usuario1")]
    [InlineData("qa.admin.xpay")]
    [InlineData("qa.comercio1")]
    public void Variantes_que_normalizan_a_qa_punto_son_reservadas(string usuario)
        => Assert.True(NombreUsuarioReservado.EsReservadoQa(usuario));

    [Theory]
    [InlineData("qa")]
    [InlineData("qax")]
    [InlineData("qa_cli_123")]
    [InlineData("qa-x")]
    [InlineData("q.a.x")]
    [InlineData("usuario.qa")]
    [InlineData("xqa.x")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("ｑａ.x")] // ｑａ.x (ancho completo): cadena distinta
    [InlineData("​qa.x")]      // espacio de ancho cero: no lo quita Trim
    public void Nombres_no_reservados(string? usuario)
        => Assert.False(NombreUsuarioReservado.EsReservadoQa(usuario));

    // Misma forma que persisten los servicios (Trim + minúsculas), sin cultura.
    [Theory]
    [InlineData("  QA.Usuario1 ", "qa.usuario1")]
    [InlineData("Usuario.Normal", "usuario.normal")]
    [InlineData(null, "")]
    public void Normalizar_es_trim_y_minusculas_invariantes(string? usuario, string esperado)
        => Assert.Equal(esperado, NombreUsuarioReservado.Normalizar(usuario));

    [Fact]
    public void Reserva_no_depende_de_la_cultura_actual()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.True(NombreUsuarioReservado.EsReservadoQa("QA.X"));
            Assert.Equal("qa.x", NombreUsuarioReservado.Normalizar(" QA.X "));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    // ── Helper: frontera de ambiente ───────────────────────────────────────
    private static readonly string[] Productivos = { "Production", "Staging", "", " ", "qa", "development", "Desconocido" };
    private static readonly string[] NoProductivos = { "Development", "QA", "Test" };

    public static TheoryData<string, string> ProductivoReservado()
    {
        var data = new TheoryData<string, string>();
        foreach (var amb in Productivos)
            foreach (var usuario in new[] { "qa.x", "QA.x", " Qa.X " })
                data.Add(amb, usuario);
        return data;
    }

    [Theory]
    [MemberData(nameof(ProductivoReservado))]
    public void Productivo_no_permite_crear_reservado(string ambiente, string usuario)
        => Assert.False(NombreUsuarioReservado.PermiteCrear(usuario, AmbienteXpayTests.Ambiente(ambiente)));

    public static TheoryData<string, string?> ProductivoNoReservado()
    {
        var data = new TheoryData<string, string?>();
        foreach (var amb in Productivos)
            foreach (var usuario in new[] { "usuario.normal", "qa_cli_123", "qa", null })
                data.Add(amb, usuario);
        return data;
    }

    [Theory]
    [MemberData(nameof(ProductivoNoReservado))]
    public void Productivo_permite_crear_no_reservado(string ambiente, string? usuario)
        => Assert.True(NombreUsuarioReservado.PermiteCrear(usuario, AmbienteXpayTests.Ambiente(ambiente)));

    public static TheoryData<string, string> NoProductivoReservado()
    {
        var data = new TheoryData<string, string>();
        foreach (var amb in NoProductivos)
            foreach (var usuario in new[] { "qa.x", "QA.x", " Qa.X " })
                data.Add(amb, usuario);
        return data;
    }

    [Theory]
    [MemberData(nameof(NoProductivoReservado))]
    public void No_productivo_permite_crear_reservado(string ambiente, string usuario)
        => Assert.True(NombreUsuarioReservado.PermiteCrear(usuario, AmbienteXpayTests.Ambiente(ambiente)));

    [Fact]
    public void Nombre_Development_con_frontera_productiva_no_permite_reservado()
        => Assert.False(NombreUsuarioReservado.PermiteCrear(
            "qa.x", new AmbienteFalso(AmbienteXpay.Development, permiteNoProductivos: false)));

    [Fact]
    public void Nombre_Production_con_frontera_no_productiva_permite_reservado()
        => Assert.True(NombreUsuarioReservado.PermiteCrear(
            "qa.x", new AmbienteFalso("Production", permiteNoProductivos: true)));

    [Fact]
    public void Ambiente_nulo_lanza_ArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => NombreUsuarioReservado.PermiteCrear("qa.x", null!));

    [Fact]
    public void Helper_no_depende_de_configuracion_ni_host()
    {
        var tipo = typeof(NombreUsuarioReservado);
        Assert.True(tipo.IsAbstract && tipo.IsSealed); // clase estática

        var tipos = tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Concat(tipo.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Select(f => f.FieldType))
            .ToList();

        Assert.DoesNotContain(tipos, t => typeof(IConfiguration).IsAssignableFrom(t));
        Assert.DoesNotContain(tipos, t => typeof(IHostEnvironment).IsAssignableFrom(t));
    }

    // ── Servicios: la guarda rechaza ANTES de tocar la BD ──────────────────
    private static RegistroInicialService Inicial(string ambiente) =>
        new(null!, AmbienteXpayTests.Ambiente(ambiente));

    private static RegistroUsuarioFinalService Final(string ambiente) =>
        new(null!, AmbienteXpayTests.Ambiente(ambiente));

    private static RegistroInicialRequest ReqInicial(string usuario) => new(usuario, "Clave-Segura-717!", "3000000000");

    private static RegistroUsuarioFinalRequest ReqFinal(string usuario) => new()
    {
        IdUnidadNegocio = 1, TipoDocumento = "CC", NumeroDocumento = "123456",
        PrimerNombre = "Sintetico", PrimerApellido = "Prueba", Celular = "3000000000",
        Usuario = usuario, Password = "Clave-Segura-717!",
    };

    [Theory]
    [InlineData("qa.algo")]
    [InlineData("QA.algo")]
    [InlineData(" Qa.algo ")]
    public async Task Produccion_RegistroInicial_rechaza_reservado_sin_tocar_BD(string usuario)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inicial("Production").RegistrarAsync(ReqInicial(usuario)));

        Assert.Equal(RegistroInicialService.MensajeUsuarioNoDisponible, ex.Message);
    }

    [Theory]
    [InlineData("qa.algo")]
    [InlineData("QA.algo")]
    [InlineData(" Qa.algo ")]
    public async Task Produccion_RegistroUsuarioFinal_rechaza_reservado_sin_tocar_BD(string usuario)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Final("Production").RegistrarAsync(ReqFinal(usuario)));

        Assert.Equal(RegistroUsuarioFinalService.MensajeUsuarioNoDisponible, ex.Message);
    }

    [Fact]
    public void Mensaje_no_revela_reserva_ni_ambiente()
    {
        Assert.Equal("El nombre de usuario no está disponible.", RegistroInicialService.MensajeUsuarioNoDisponible);
        Assert.Equal(RegistroInicialService.MensajeUsuarioNoDisponible, RegistroUsuarioFinalService.MensajeUsuarioNoDisponible);
    }

    // Sondas: cuando la guarda no rechaza, el servicio continúa hasta la BD
    // (null → NullReferenceException). No representan comportamiento deseado.
    [Theory]
    [InlineData("QA", "qa.algo")]
    [InlineData("Development", "QA.algo")]
    [InlineData("Production", "usuario.normal")]
    public async Task RegistroInicial_continua_tras_la_guarda_cuando_corresponde(string ambiente, string usuario)
        => await Assert.ThrowsAsync<NullReferenceException>(
            () => Inicial(ambiente).RegistrarAsync(ReqInicial(usuario)));

    [Theory]
    [InlineData("QA", "qa.algo")]
    [InlineData("Test", "QA.algo")]
    [InlineData("Production", "usuario.normal")]
    public async Task RegistroUsuarioFinal_continua_tras_la_guarda_cuando_corresponde(string ambiente, string usuario)
        => await Assert.ThrowsAsync<NullReferenceException>(
            () => Final(ambiente).RegistrarAsync(ReqFinal(usuario)));

    [Theory]
    [InlineData(typeof(RegistroInicialService))]
    [InlineData(typeof(RegistroUsuarioFinalService))]
    public void Constructor_publico_recibe_XpayDbContext_e_IAmbienteXpay(Type servicio)
    {
        var publico = Assert.Single(servicio.GetConstructors());

        Assert.Equal(new[] { typeof(XpayDbContext), typeof(IAmbienteXpay) },
            publico.GetParameters().Select(p => p.ParameterType));
    }

    private sealed class AmbienteFalso(string nombre, bool permiteNoProductivos) : IAmbienteXpay
    {
        public string Nombre => nombre;
        public bool PermiteMecanismosNoProductivos => permiteNoProductivos;
        public bool EsProductivo => !permiteNoProductivos;
    }
}
