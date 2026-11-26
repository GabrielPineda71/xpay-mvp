using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

public sealed class CarteraPilotoOperacionTests
{
    [Theory]
    [InlineData("QA", true)]
    [InlineData("Test", true)]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("qa", false)]
    [InlineData("Staging", false)]
    public async Task Solo_ambiente_no_productivo_conserva_compuerta_QA_sin_bandera_productiva(string nombre, bool continua)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["XPAY_OPERACIONES_FINANCIERAS_HABILITADAS"] = "true" }).Build());
        services.AddSingleton<ControlOperacionesFinancieras>();
        if (nombre is not null) services.AddSingleton(AmbienteXpayTests.Ambiente(nombre));
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider, User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim("idUsuario", "7") }, "test")) };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var filters = new List<IFilterMetadata>();
        var context = new ResourceExecutingContext(action, filters, new List<IValueProviderFactory>());
        var llamadas = 0;
        await new CarteraPilotoOperacionAttribute().OnResourceExecutionAsync(context, () =>
        {
            llamadas++;
            return Task.FromResult(new ResourceExecutedContext(action, filters));
        });
        Assert.Equal(continua ? 1 : 0, llamadas);
        if (continua) Assert.Null(context.Result);
        else Assert.Equal(503, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public void Ausencia_de_autoridad_no_permite_compuerta_QA() =>
        Assert.False(CarteraPilotoOperacionAttribute.UsaCompuertaQa(null));
}
