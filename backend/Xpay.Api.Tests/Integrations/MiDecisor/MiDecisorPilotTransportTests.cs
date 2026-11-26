using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

public sealed class MiDecisorPilotTransportTests
{
    private static FakeConfiguration Config() => new(new Dictionary<string,string?>
    {
        [MiDecisorOptions.EnvBaseUrl]="https://midecisor.test",
        [MiDecisorOptions.EnvClientId]="synthetic-client", [MiDecisorOptions.EnvClientSecret]="synthetic-secret",
        [MiDecisorOptions.EnvUsername]="synthetic-user", [MiDecisorOptions.EnvPassword]="synthetic-password",
    });

    [Fact]
    public async Task BufferedJsonPreservesBytesAndSetsExactMediaTypeAndLength()
    {
        var dto = new MiDecisorConsultaRequest("1", "1234567", "DE LA PEÑA");
        using var original = JsonContent.Create(dto);
        var expected = await original.ReadAsByteArrayAsync();
        using var actual = await MiDecisorJsonTransport.CreateContentAsync(dto, 4096, default);
        Assert.Equal(expected, await actual.ReadAsByteArrayAsync());
        Assert.Equal("application/json", actual.Headers.ContentType!.ToString());
        Assert.Equal(expected.Length, actual.Headers.ContentLength);
    }

    [Fact]
    public async Task RequestAndResponseLimitsAreEnforced()
    {
        await Assert.ThrowsAsync<MiDecisorRequestValidationException>(() =>
            MiDecisorJsonTransport.CreateContentAsync(new { value=new string('X', 4096) }, 4096, default));
        using var body = new StringContent(new string('X', 4097));
        await Assert.ThrowsAsync<MiDecisorProtocolException>(() =>
            MiDecisorJsonTransport.ReadJsonAsync<object>(body, new(), 4096, default));
    }

    [Fact]
    public async Task AuthAndQueryUseFixedJsonHeadersAndCallbackOnlyAfterAuth()
    {
        var calls = new List<string>();
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.ToString());
            Assert.True(request.Content.Headers.ContentLength > 0);
            Assert.False(request.Headers.TransferEncodingChunked);
            Assert.Contains(request.Headers.Accept, a => a.MediaType=="application/json");
            var auth = request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal);
            calls.Add(auth ? "auth" : "query");
            return Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, auth
                ? "{\"access_token\":\"synthetic-token\",\"expires_in\":\"3600\"}"
                : "{\"status\":\"ACCEPTED\",\"content\":{\"respuesta\":{\"informacionRiesgo\":{\"conInformacion\":false}}}}"));
        });
        var factory = new FakeHttpClientFactory(handler);
        var token = new MiDecisorTokenProvider(factory, Config(), NullLogger<MiDecisorTokenProvider>.Instance, TimeProvider.System);
        var client = new MiDecisorClient(factory, token, Config(), NullLogger<MiDecisorClient>.Instance);
        await client.ConsultarPersonaNaturalAsync(new("1","1234567","SINTETICO"), _ =>
        { calls.Add("durable-boundary"); return Task.CompletedTask; });
        Assert.Equal(new[] { "auth", "durable-boundary", "query" }, calls);
    }

    [Fact]
    public async Task FailedAuthNeverCrossesDurableQueryBoundaryOrSendsQuery()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.UnsupportedMediaType,"DO_NOT_READ"));
        var factory = new FakeHttpClientFactory(handler);
        var token = new MiDecisorTokenProvider(factory, Config(), NullLogger<MiDecisorTokenProvider>.Instance, TimeProvider.System);
        var client = new MiDecisorClient(factory, token, Config(), NullLogger<MiDecisorClient>.Instance);
        var boundary=0;
        var ex = await Assert.ThrowsAsync<MiDecisorTransportException>(() => client.ConsultarPersonaNaturalAsync(
            new("1","1234567","SINTETICO"), _ => { boundary++; return Task.CompletedTask; }));
        Assert.Equal(MiDecisorEtapa.Autenticacion, ex.Etapa);
        Assert.Equal(0,boundary);
        Assert.Equal(1,handler.CallCount);
        Assert.DoesNotContain("DO_NOT_READ", ex.ToString());
    }

    [Fact]
    public async Task PostStartsAreSeparatedAndCanceledQueueReleasesCapacity()
    {
        var limiter = new MiDecisorPostRateLimiter(TimeProvider.System);
        var start = TimeProvider.System.GetTimestamp();
        using var first = await limiter.EnterAsync(default);
        using var second = await limiter.EnterAsync(default);
        Assert.True(TimeProvider.System.GetElapsedTime(start) >= TimeSpan.FromSeconds(1));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var _ = await limiter.EnterAsync(canceled.Token); });
    }

    [Fact]
    public async Task BoundedQueueRejectsFiftyFirstWaiterBeforeItsDurableCallback()
    {
        var limiter=new MiDecisorPostRateLimiter(TimeProvider.System);
        using var first=await limiter.EnterAsync(default);
        using var second=await limiter.EnterAsync(default);
        using var cancellation=new CancellationTokenSource();
        var queued=Enumerable.Range(0,50).Select(_=>limiter.EnterAsync(cancellation.Token).AsTask()).ToArray();
        var callback=0;
        await Assert.ThrowsAsync<MiDecisorRateLimitException>(async ()=>
        { using var _=await limiter.EnterAsync(default,_=>{callback++;return Task.CompletedTask;}); });
        Assert.Equal(0,callback);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Task.WhenAll(queued));
        // Every canceled waiter released its counter: a fresh canceled caller
        // fails by cancellation, not a stale full-queue rejection.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async ()=>
        { using var _=await limiter.EnterAsync(cancellation.Token); });
    }

    [Fact]
    public async Task TokenRefreshWaitersAreBoundedAndCanceledWaitersReleaseTheirCounter()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new FakeHttpMessageHandler(async (_,ct)=>
        { entered.SetResult(); return await response.Task.WaitAsync(ct); });
        var provider=new MiDecisorTokenProvider(new FakeHttpClientFactory(handler),Config(),NullLogger<MiDecisorTokenProvider>.Instance,TimeProvider.System);
        var first=provider.GetAccessTokenAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancellation=new CancellationTokenSource();
        var queued=Enumerable.Range(0,49).Select(_=>provider.GetAccessTokenAsync(cancellation.Token)).ToArray();
        var ex=await Assert.ThrowsAsync<MiDecisorRateLimitException>(()=>provider.GetAccessTokenAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(MiDecisorEtapa.Autenticacion,ex.Etapa);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Task.WhenAll(queued));
        response.SetResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK,"{\"access_token\":\"synthetic-token\",\"expires_in\":\"3600\"}"));
        Assert.Equal("synthetic-token",await first);
        Assert.Equal("synthetic-token",await provider.GetAccessTokenAsync());
        Assert.Equal(1,handler.CallCount);
    }

    [Fact]
    public async Task RateLeaseCoversSlowResponseBodiesAndActualSendStarts()
    {
        var starts=new List<long>(); var sync=new object();
        var twoHeaders=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBodies=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new FakeHttpMessageHandler((_,_)=>
        {
            lock(sync)
            {
                starts.Add(TimeProvider.System.GetTimestamp());
                if(starts.Count==2) twoHeaders.TrySetResult();
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new GatedJsonContent(releaseBodies.Task) });
        });
        var client=new MiDecisorClient(new FakeHttpClientFactory(handler),new FakeMiDecisorTokenProvider(),Config(),
            NullLogger<MiDecisorClient>.Instance,new MiDecisorPostRateLimiter(TimeProvider.System));
        var tasks=Enumerable.Range(0,3).Select(_=>client.ConsultarPersonaNaturalAsync(new("1","1234567","SINTETICO"))).ToArray();
        await twoHeaders.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await Task.Delay(1100);
        try { Assert.Equal(2,handler.CallCount); }
        finally { releaseBodies.TrySetResult(); }
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(3,handler.CallCount);
        for(var i=1;i<starts.Count;i++)
            Assert.True(TimeProvider.System.GetElapsedTime(starts[i-1],starts[i])>=TimeSpan.FromSeconds(1));
    }

    private sealed class GatedJsonContent(Task release) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken ct)
            => Task.FromResult<Stream>(new GatedStream(release));
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length=0;return false; }
        private sealed class GatedStream(Task release) : MemoryStream(Encoding.UTF8.GetBytes(
            "{\"status\":\"ACCEPTED\",\"content\":{\"respuesta\":{\"informacionRiesgo\":{\"conInformacion\":false}}}}"))
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
            { await release.WaitAsync(ct); return await base.ReadAsync(buffer,ct); }
        }
    }
}
