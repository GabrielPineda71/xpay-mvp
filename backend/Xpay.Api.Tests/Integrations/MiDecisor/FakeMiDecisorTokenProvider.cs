using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

// Fake de IMiDecisorTokenProvider para los tests de M2.2. Nunca un token real:
// devuelve un valor sintético fijo, o lanza una excepción sintética. Cuenta
// las llamadas y honra la cancelación del caller.
internal sealed class FakeMiDecisorTokenProvider : IMiDecisorTokenProvider
{
    public const string SyntheticToken = "test-access-token";

    private readonly string _token;
    private readonly Exception? _toThrow;

    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);

    public FakeMiDecisorTokenProvider(string token = SyntheticToken, Exception? toThrow = null)
    {
        _token   = token;
        _toThrow = toThrow;
    }

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        cancellationToken.ThrowIfCancellationRequested();

        return _toThrow is not null
            ? Task.FromException<string>(_toThrow)
            : Task.FromResult(_token);
    }
}
