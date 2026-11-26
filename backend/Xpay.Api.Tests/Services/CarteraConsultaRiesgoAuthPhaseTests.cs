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
    private static CarteraConsultaRiesgoService Service(ICarteraConsultaRiesgoStore store,IMiDecisorClient client)
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

    [Fact]
    public async Task AuthorizationRevokedWhileWaitingForTokenNeverSendsCreditPost()
    {
        var inner=Store(); var vigente=true;
        var store=new RevalidatingStore(inner,()=>vigente);
        var token=new WaitingToken();
        var handler=new FakeHttpMessageHandler(()=>throw new InvalidOperationException("Credit POST must not run."));
        var client=new MiDecisorClient(new FakeHttpClientFactory(handler),token,Config(),NullLogger<MiDecisorClient>.Instance);
        var execution=Service(store,client).EjecutarConsultaRiesgoAsync(10,7,"synthetic-correlation");
        await token.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vigente=false;
        token.Release.TrySetResult();
        var result=await execution.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1,token.Calls); Assert.Equal(0,handler.CallCount); Assert.Equal(0,inner.MarcarCalls);
        Assert.Equal(1,store.RevalidationCalls);
        Assert.Equal(CarteraConsultaRiesgoResultados.ErrorValidacionLocal,result.ResultadoTecnico);
        Assert.Equal(CarteraIntentoFases.PreCall,inner.FaseAlFinalizar);
        Assert.True(inner.UltimoOutcome!.FinalizacionDesdePreCall);
        Assert.Null(inner.UltimoOutcome.HttpStatusObservado); Assert.Null(inner.UltimoOutcome.ScoreRaw);
    }

    private sealed class WaitingToken : IMiDecisorTokenProvider
    {
        internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        public async Task<string> GetAccessTokenAsync(CancellationToken ct=default)
        { Calls++; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return "synthetic-token"; }
    }
    private sealed class RevalidatingStore(FakeCarteraConsultaRiesgoStore inner,Func<bool> vigente) : ICarteraConsultaRiesgoStore
    {
        internal int RevalidationCalls;
        public Task<ConsultaRiesgoContexto?> CargarContextoAsync(long s,long u,CancellationToken ct)=>inner.CargarContextoAsync(s,u,ct);
        public Task<bool> IntentarIniciarConsultaAsync(long s,long u,DateTime fecha,CancellationToken ct)=>inner.IntentarIniciarConsultaAsync(s,u,fecha,ct);
        public Task MarcarEnvioInciertoAsync(long s,long u,DateTime fecha,CancellationToken ct)
        {
            RevalidationCalls++;
            if(!vigente())throw new MiDecisorRequestValidationException("Autorización actual no vigente.");
            return inner.MarcarEnvioInciertoAsync(s,u,fecha,ct);
        }
        public Task FinalizarIntentoAsync(long s,long u,ResultadoIntentoDurable outcome,CancellationToken ct)=>inner.FinalizarIntentoAsync(s,u,outcome,ct);
    }
}
