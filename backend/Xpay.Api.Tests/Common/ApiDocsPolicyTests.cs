using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

// XPAY-717 — Swagger/API Docs subordinados a la frontera central IAmbienteXpay.
// Tests puros: sin host, DB ni red. Los ambientes se construyen con el
// AmbienteXpay real (AmbienteXpayTests.Ambiente), no con otra lista.
public sealed class ApiDocsPolicyTests
{
    private static readonly string[] NombresProductivos =
    {
        "Production", "Staging", "", " ", "qa", "Qa", "QA ", " QA",
        "development", "TEST", "Prod", "QA,Production", "Desconocido",
    };

    private static readonly bool?[] ValoresConfig = { true, false, null };

    // A. Productivo × {true, false, null} → siempre false.
    public static TheoryData<string, bool?> MatrizProductiva()
    {
        var data = new TheoryData<string, bool?>();
        foreach (var nombre in NombresProductivos)
            foreach (var config in ValoresConfig)
                data.Add(nombre, config);
        return data;
    }

    [Theory]
    [MemberData(nameof(MatrizProductiva))]
    public void Ambiente_productivo_nunca_expone_docs(string nombre, bool? config)
        => Assert.False(ApiDocsPolicy.SwaggerHabilitado(AmbienteXpayTests.Ambiente(nombre), config));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void Nombre_de_ambiente_nulo_nunca_expone_docs(bool? config)
    {
        var ambiente = new AmbienteXpay(new AmbienteXpayTests.FakeHostEnvironment(null!));

        Assert.False(ApiDocsPolicy.SwaggerHabilitado(ambiente, config));
    }

    // B. No productivo: la configuración decide; sin config sólo Development.
    [Theory]
    [InlineData("Development", true,  true)]
    [InlineData("Development", false, false)]
    [InlineData("Development", null,  true)]
    [InlineData("QA",          true,  true)]
    [InlineData("QA",          false, false)]
    [InlineData("QA",          null,  false)]
    [InlineData("Test",        true,  true)]
    [InlineData("Test",        false, false)]
    [InlineData("Test",        null,  false)]
    public void Ambiente_no_productivo_respeta_config_y_fallback_development(
        string nombre, bool? config, bool esperado)
        => Assert.Equal(esperado, ApiDocsPolicy.SwaggerHabilitado(AmbienteXpayTests.Ambiente(nombre), config));

    // C. La frontera manda sobre el nombre: aunque se llame "Development",
    // si la frontera dice productivo no hay docs.
    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void Frontera_productiva_prevalece_sobre_el_nombre(bool? config)
    {
        var ambiente = new AmbienteFalso(AmbienteXpay.Development, permiteNoProductivos: false);

        Assert.False(ApiDocsPolicy.SwaggerHabilitado(ambiente, config));
    }

    // D. Argumento nulo.
    [Fact]
    public void Ambiente_nulo_lanza_ArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => ApiDocsPolicy.SwaggerHabilitado(null!, true));

    // E. Evidencia estructural: sin IConfiguration ni IHostEnvironment.
    [Fact]
    public void Politica_no_depende_de_configuracion_ni_host()
    {
        var tipo = typeof(ApiDocsPolicy);
        Assert.True(tipo.IsAbstract && tipo.IsSealed); // clase estática: sin estado ni constructor

        var parametros = tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();
        var campos = tipo.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(f => f.FieldType)
            .ToList();

        Assert.DoesNotContain(parametros.Concat(campos), t => typeof(IConfiguration).IsAssignableFrom(t));
        Assert.DoesNotContain(parametros.Concat(campos), t => typeof(IHostEnvironment).IsAssignableFrom(t));

        var firma = tipo.GetMethod(nameof(ApiDocsPolicy.SwaggerHabilitado))!.GetParameters();
        Assert.Equal(new[] { typeof(IAmbienteXpay), typeof(bool?) }, firma.Select(p => p.ParameterType));
    }

    private sealed class AmbienteFalso(string nombre, bool permiteNoProductivos) : IAmbienteXpay
    {
        public string Nombre => nombre;
        public bool PermiteMecanismosNoProductivos => permiteNoProductivos;
        public bool EsProductivo => !permiteNoProductivos;
    }
}
