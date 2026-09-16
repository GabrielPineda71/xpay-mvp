using Xpay.Api.Integrations.Passport;

namespace Xpay.Api.Tests.Services;

// XPAY-381 — fake de IPassportPaymentClient para tests offline de
// BrebPaymentReconciliationBatch. NUNCA red, NUNCA HttpClient.
//
// CreateBrebPaymentAsync lanza a propósito (NotImplementedException): el
// reconciliador automático NUNCA debe crear un Payment — este fake hace que
// cualquier intento accidental de invocarlo falle el test ruidosamente, en
// vez de devolver silenciosamente un resultado (mismo patrón ya usado por
// FakePassportCustomerAccountClient para las operaciones que
// CuentaOperativaService nunca debe llamar).
internal sealed class FakePassportPaymentClient : IPassportPaymentClient
{
    private readonly Dictionary<string, PassportPaymentResponse> _respuestas;
    private readonly Dictionary<string, Exception> _excepciones;
    private readonly List<string> _paymentIdsConsultados = new();

    public IReadOnlyList<string> PaymentIdsConsultados => _paymentIdsConsultados;

    public FakePassportPaymentClient(
        Dictionary<string, PassportPaymentResponse>? respuestas = null,
        Dictionary<string, Exception>? excepciones = null)
    {
        _respuestas  = respuestas  ?? new Dictionary<string, PassportPaymentResponse>();
        _excepciones = excepciones ?? new Dictionary<string, Exception>();
    }

    public Task<PassportPaymentResponse> RetrievePaymentAsync(
        string paymentId, CancellationToken cancellationToken = default)
    {
        _paymentIdsConsultados.Add(paymentId);

        if (_excepciones.TryGetValue(paymentId, out var ex))
            throw ex;

        if (_respuestas.TryGetValue(paymentId, out var respuesta))
            return Task.FromResult(respuesta);

        throw new InvalidOperationException(
            $"FakePassportPaymentClient sin respuesta/excepción configurada para paymentId='{paymentId}'.");
    }

    public Task<PassportPaymentResponse> CreateBrebPaymentAsync(
        PassportCreatePaymentRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(
            "XPAY-381: BrebPaymentReconciliationBatch nunca debe llamar CreateBrebPaymentAsync — solo RetrievePaymentAsync.");
}
