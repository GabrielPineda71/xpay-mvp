using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class CarteraConsultaRiesgoAuthPhaseTests
{
    private static FakeConfiguration Config() => new(new Dictionary<string,string?>
    {
        [MiDecisorOptions.EnvBaseUrl]="https://midecisor.test",
        [MiDecisorOptions.EnvClientId]="synthetic-client",[MiDecisorOptions.EnvClientSecret]="synthetic-secret",
        [MiDecisorOptions.EnvUsername]="synthetic-user",[MiDecisorOptions.EnvPassword]="synthetic-password",
    });
    private static FakeCarteraConsultaRiesgoStore Store() => new()
    {
        Contexto=new(10,7,3,CarteraSolicitudCupoEstados.Recibida,new Persona
        { IdPersona=3,TipoDocumento="CC",NumeroDocumento="1234567",PrimerApellido="SINTETICO" }),
    };
    private static CarteraConsultaRiesgoService Service(FakeCarteraConsultaRiesgoStore store,IMiDecisorClient client)
        => new(store,client,new FakeConsultaRiesgoAutorizacion(true),TimeProvider.System,NullLogger<CarteraConsultaRiesgoService>.Instance);

    [Theory]
    [InlineData(415)] [InlineData(401)] [InlineData(500)]
    public async Task AuthHttpFailureFinalizesPreCallWithoutMarkingBureauSend(int status)
    {
        var handler=new FakeHttpMessageHandler(()=>FakeHttpMessageHandler.Json((HttpStatusCode)status,"SECRET_DO_NOT_READ"));
        var factory=new FakeHttpClientFactory(handler);
        var token=new MiDecisorTokenProvider(factory,Config(),NullLogger<MiDecisorTokenProvider>.Instance,TimeProvider.System);
        var client=new MiDecisorClient(factory,token,Config(),NullLogger<MiDecisorClient>.Instance);
        var store=Store();
        var result=await Service(store,client).EjecutarConsultaRiesgoAsync(10,7,"synthetic-correlation");
        Assert.Equal(CarteraConsultaRiesgoResultados.ErrorAutenticacion,result.ResultadoTecnico);
        Assert.Equal(1,handler.CallCount); Assert.Equal(0,store.MarcarCalls);
        Assert.Equal(CarteraIntentoFases.PreCall,store.FaseAlFinalizar);
        Assert.True(store.UltimoOutcome!.FinalizacionDesdePreCall);
        Assert.Null(store.UltimoOutcome.HttpStatusObservado);Assert.Null(store.UltimoOutcome.ScoreRaw);
    }

    [Fact]
    public async Task AuthNetworkFailureAndMalformedTokenCannotBeClassifiedAsCreditUncertainty()
    {
        foreach (var handler in new[]
        {
            new FakeHttpMessageHandler((_,_)=>throw new HttpRequestException("SECRET_MARKER")),
            new FakeHttpMessageHandler(()=>FakeHttpMessageHandler.Json(HttpStatusCode.OK,"{}")),
        })
        {
            var factory=new FakeHttpClientFactory(handler);
            var token=new MiDecisorTokenProvider(factory,Config(),NullLogger<MiDecisorTokenProvider>.Instance,TimeProvider.System);
            var client=new MiDecisorClient(factory,token,Config(),NullLogger<MiDecisorClient>.Instance);
            var store=Store();
            var result=await Service(store,client).EjecutarConsultaRiesgoAsync(10,7,"synthetic-correlation");
            Assert.Equal(CarteraConsultaRiesgoResultados.ErrorAutenticacion,result.ResultadoTecnico);
            Assert.Equal(0,store.MarcarCalls);Assert.True(store.UltimoOutcome!.FinalizacionDesdePreCall);
            Assert.Equal(1,handler.CallCount);
        }
    }

    [Fact]
    public async Task QueryNetworkFailurePreservesDurableUncertaintyAndDoesNotRetry()
    {
        var store=Store();
        var handler=new FakeHttpMessageHandler((_,_)=>
        {
            Assert.Equal(1,store.MarcarCalls);
            throw new HttpRequestException("SECRET_MARKER");
        });
        var client=new MiDecisorClient(new FakeHttpClientFactory(handler),new FakeMiDecisorTokenProvider(),Config(),NullLogger<MiDecisorClient>.Instance);
        var result=await Service(store,client).EjecutarConsultaRiesgoAsync(10,7,"synthetic-correlation");
        Assert.Equal(CarteraConsultaRiesgoResultados.ResultadoIncierto,result.ResultadoTecnico);
        Assert.Equal(1,handler.CallCount); Assert.False(store.UltimoOutcome!.FinalizacionDesdePreCall);
        Assert.Equal(CarteraIntentoFases.EnvioIncierto,store.FaseAlFinalizar);
    }

    [Fact]
    public async Task PreCallFinalizerRejectsAnyFabricatedProviderResultBeforeDatabaseAccess()
    {
        var store=new CarteraConsultaRiesgoStore(null!);
        var outcome=new ResultadoIntentoDurable(CarteraSolicitudCupoEstados.EnEvaluacion,CarteraConsultaRiesgoResultados.Aceptada,
            null,null,false,DateTime.UtcNow,null,null,null,null,null,null) { FinalizacionDesdePreCall=true };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.FinalizarIntentoAsync(10,7,outcome,default));
    }
}
