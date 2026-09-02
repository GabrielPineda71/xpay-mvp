using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Tests.Services;

// Fake de IMiDecisorClient para los tests de M2.3a. NUNCA red, NUNCA HttpClient,
// NUNCA token provider. Cuenta invocaciones, guarda la última petición, y
// devuelve un resultado configurado o lanza una excepción configurada.
internal sealed class FakeMiDecisorClient : IMiDecisorClient
{
    private readonly MiDecisorResultado? _resultado;
    private readonly Exception? _toThrow;
    private readonly Func<CancellationToken, Task>? _beforeReturn;

    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);
    public MiDecisorConsultaRequest? LastRequest { get; private set; }

    public FakeMiDecisorClient(
        MiDecisorResultado? resultado = null,
        Exception? toThrow = null,
        Func<CancellationToken, Task>? beforeReturn = null)
    {
        _resultado    = resultado;
        _toThrow      = toThrow;
        _beforeReturn = beforeReturn;
    }

    public async Task<MiDecisorResultado> ConsultarPersonaNaturalAsync(
        MiDecisorConsultaRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        LastRequest = request;

        if (_beforeReturn is not null)
            await _beforeReturn(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (_toThrow is not null)
            throw _toThrow;

        return _resultado
            ?? throw new InvalidOperationException("FakeMiDecisorClient sin resultado ni excepción configurados.");
    }
}
