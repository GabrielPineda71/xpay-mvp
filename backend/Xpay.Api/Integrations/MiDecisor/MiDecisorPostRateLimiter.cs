using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xpay.Api.Integrations.MiDecisor;

// Shared per worker by token and query requests. Starts are >=1 monotonic second
// apart, at most two requests are in flight, and at most 50 callers can queue.
public sealed class MiDecisorPostRateLimiter(TimeProvider clock)
{
    private readonly SemaphoreSlim _active = new(2, 2);
    private readonly SemaphoreSlim _start = new(1, 1);
    private long? _lastStart;
    private int _queued;

    internal async ValueTask<IDisposable> EnterAsync(CancellationToken ct, Func<CancellationToken,Task>? beforeStart = null,
        Action? start = null)
    {
        if (Interlocked.Increment(ref _queued) > 50)
        {
            Interlocked.Decrement(ref _queued);
            throw new MiDecisorRateLimitException();
        }
        var active = false;
        try
        {
            await _active.WaitAsync(ct).ConfigureAwait(false);
            active = true;
            await _start.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                while (_lastStart is { } previous)
                {
                    var remaining = TimeSpan.FromSeconds(1) - clock.GetElapsedTime(previous, clock.GetTimestamp());
                    if (remaining <= TimeSpan.Zero) break;
                    await Task.Delay(remaining, clock, ct).ConfigureAwait(false);
                }
                if (beforeStart is not null) await beforeStart(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                // Invoke SendAsync while still holding the start lock. Stamp
                // afterwards: scheduling or synchronous work cannot compress
                // the gap between actual invocations of consecutive POSTs.
                try { start?.Invoke(); }
                finally { _lastStart = clock.GetTimestamp(); }
            }
            finally { _start.Release(); }
            return new ActiveLease(_active);
        }
        catch
        {
            if (active) _active.Release();
            throw;
        }
        finally { Interlocked.Decrement(ref _queued); }
    }

    private sealed class ActiveLease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) semaphore.Release(); }
    }

    // Owned by the complete operation, including response-body reading.
    internal sealed class LeaseScope : IDisposable
    {
        internal IDisposable? Value { get; set; }
        public void Dispose() { Value?.Dispose(); Value = null; }
    }
}

public static class MiDecisorServiceCollectionExtensions
{
    public static IServiceCollection AddMiDecisorTransport(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<MiDecisorPostRateLimiter>();
        services.AddHttpClient(MiDecisorJsonTransport.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                MaxConnectionsPerServer = 2,
            })
            .RedactLoggedHeaders(static _ => true)
            .RemoveAllLoggers();
        services.AddHttpClient(MiDecisorIdentidadResolver.VeriffClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(15), MaxConnectionsPerServer = 2,
            })
            .RedactLoggedHeaders(static _ => true)
            .RemoveAllLoggers();
        return services;
    }

    public static IServiceCollection AddMiDecisorIdentidadProduccion(this IServiceCollection services)
    {
        services.AddScoped<IMiDecisorIdentidadResolver, MiDecisorIdentidadResolver>();
        services.AddScoped<IMiDecisorConsultaExternaStore, MiDecisorConsultaExternaStore>();
        return services;
    }
}
