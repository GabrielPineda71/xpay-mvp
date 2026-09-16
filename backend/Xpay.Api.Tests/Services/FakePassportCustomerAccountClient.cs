using Xpay.Api.Integrations.Passport;

namespace Xpay.Api.Tests.Services;

// XPAY-372 — fake de IPassportCustomerAccountClient para tests offline de
// CuentaOperativaService. NUNCA red, NUNCA HttpClient. Mismo patrón que
// FakeMiDecisorClient.
//
// LinkMerchantAsync/RetrieveCustomerAsync/LinkAccountAsync lanzan a
// propósito (NotImplementedException): XPAY-372 exige explícitamente que
// ningún código productivo llame estas tres operaciones — este fake hace
// que cualquier intento accidental de invocarlas falle el test
// ruidosamente, en vez de devolver silenciosamente un resultado vacío.
internal sealed class FakePassportCustomerAccountClient : IPassportCustomerAccountClient
{
    private readonly PassportAccountResponse? _accountResult;
    private readonly Exception? _toThrow;

    private int _retrieveAccountCallCount;
    public int RetrieveAccountCallCount => Volatile.Read(ref _retrieveAccountCallCount);
    public string? LastAccountIdRequested { get; private set; }

    public FakePassportCustomerAccountClient(PassportAccountResponse? accountResult = null, Exception? toThrow = null)
    {
        _accountResult = accountResult;
        _toThrow       = toThrow;
    }

    public Task<PassportAccountResponse> RetrieveAccountAsync(
        string accountId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _retrieveAccountCallCount);
        LastAccountIdRequested = accountId;

        if (_toThrow is not null)
            throw _toThrow;

        return Task.FromResult(_accountResult
            ?? throw new InvalidOperationException("FakePassportCustomerAccountClient sin resultado ni excepción configurados."));
    }

    public Task<PassportCustomerResponse> LinkMerchantAsync(
        PassportLinkMerchantRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("XPAY-372: CuentaOperativaService nunca debe llamar LinkMerchantAsync.");

    public Task<PassportCustomerResponse> RetrieveCustomerAsync(
        string customerId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("XPAY-372: CuentaOperativaService nunca debe llamar RetrieveCustomerAsync.");

    public Task<PassportAccountResponse> LinkAccountAsync(
        PassportLinkAccountRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("XPAY-372: CuentaOperativaService nunca debe llamar LinkAccountAsync.");
}
