using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

// XPAY-717 — frontera central QA/PROD. Lista cerrada Development/QA/Test
// (ordinal); cualquier otro nombre es productivo (fail-closed). Ninguna
// configuración participa del veredicto.
public sealed class AmbienteXpayTests
{
    internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Xpay.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    internal static IAmbienteXpay Ambiente(string nombre) => new AmbienteXpay(new FakeHostEnvironment(nombre));

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void Lista_cerrada_no_productiva_permite_mecanismos_qa(string nombre)
    {
        var ambiente = Ambiente(nombre);

        Assert.True(ambiente.PermiteMecanismosNoProductivos);
        Assert.False(ambiente.EsProductivo);
        Assert.True(AmbienteXpay.EsNoProductivo(nombre));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("qa")]
    [InlineData("Qa")]
    [InlineData("QA ")]
    [InlineData(" QA")]
    [InlineData("development")]
    [InlineData("TEST")]
    [InlineData("Prod")]
    [InlineData("QA,Production")]
    public void Cualquier_otro_nombre_es_productivo(string nombre)
    {
        var ambiente = Ambiente(nombre);

        Assert.True(ambiente.EsProductivo);
        Assert.False(ambiente.PermiteMecanismosNoProductivos);
        Assert.False(AmbienteXpay.EsNoProductivo(nombre));
    }

    [Fact]
    public void Nombre_nulo_es_productivo()
    {
        var ambiente = new AmbienteXpay(new FakeHostEnvironment(null!));

        Assert.True(ambiente.EsProductivo);
        Assert.Equal(string.Empty, ambiente.Nombre);
        Assert.False(AmbienteXpay.EsNoProductivo(null));
    }

    [Fact]
    public void Ninguna_configuracion_participa_del_veredicto()
    {
        var parametros = typeof(AmbienteXpay).GetConstructors().SelectMany(c => c.GetParameters());

        Assert.DoesNotContain(parametros, p => typeof(IConfiguration).IsAssignableFrom(p.ParameterType));
    }

    // ── Filtro SoloAmbienteNoProductivo ───────────────────────────────────
    private static ResourceExecutingContext Contexto(IAmbienteXpay? ambiente)
    {
        var services = new ServiceCollection();
        if (ambiente is not null)
            services.AddSingleton(ambiente);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(action, new List<IFilterMetadata>(), new List<IValueProviderFactory>());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("qa")]
    public void Filtro_en_productivo_responde_404(string nombre)
    {
        var ctx = Contexto(Ambiente(nombre));

        new SoloAmbienteNoProductivoAttribute().OnResourceExecuting(ctx);

        var nf = Assert.IsType<NotFoundObjectResult>(ctx.Result);
        Assert.Equal(404, nf.StatusCode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void Filtro_en_no_productivo_no_interviene(string nombre)
    {
        var ctx = Contexto(Ambiente(nombre));

        new SoloAmbienteNoProductivoAttribute().OnResourceExecuting(ctx);

        Assert.Null(ctx.Result);
    }

    [Fact]
    public void Filtro_sin_guardia_registrada_responde_404()
    {
        var ctx = Contexto(null);

        new SoloAmbienteNoProductivoAttribute().OnResourceExecuting(ctx);

        Assert.IsType<NotFoundObjectResult>(ctx.Result);
    }
}
