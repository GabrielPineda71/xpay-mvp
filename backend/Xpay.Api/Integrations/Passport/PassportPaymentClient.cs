namespace Xpay.Api.Integrations.Passport;

// XPAY-373 — implementación de IPassportPaymentClient sobre
// IPassportHttpClient (XPAY-272). Sin HttpClient propio, sin token provider
// propio — mismo patrón exacto que PassportKeyClient/
// PassportCustomerAccountClient.
//
// Los paths son constantes de contrato (confirmadas vía consulta de solo
// lectura a docs.passportfintech.com, XPAY-372/373) — NO configurables por
// entorno, mismo criterio que el resto de la integración.
//
// Validación de input: sólo guards de protocolo (evitar construir un
// request inutilizable) — la validación de NEGOCIO (resolución vigente,
// monto vs. saldo, cuenta operativa correcta) vive en
// BrebPaymentRequestBuilder (capa de servicio), no aquí — mismo criterio de
// separación ya aplicado entre PassportKeyClient y
// BrebKeyResolutionRequestBuilder (XPAY-371).
//
// Guard de protocolo post-respuesta: RequirePaymentId, análogo a
// RequireKeyId/RequireResolutionId de PassportKeyClient — CreateBrebPaymentAsync
// y RetrievePaymentAsync SIEMPRE deben devolver un `id` (payment_id); su
// ausencia es un fallo de protocolo (PassportProtocolException), nunca un
// resultado silenciosamente incompleto. Esto NO valida `status` — un status
// ausente/desconocido es responsabilidad de BrebPaymentStateMachine (nunca
// mueve dinero ante duda), no un fallo de transporte.
//
// Lifetime en DI: Singleton (Program.cs, XPAY-373) — mismo criterio que
// PassportKeyClient/PassportCustomerAccountClient: wrapper puro sin estado.
public sealed class PassportPaymentClient : IPassportPaymentClient
{
    private const string CreateBrebPaymentPath = "/v1/payments/breb";
    private const string RetrievePaymentPathTemplate = "/v1/payments/{0}";

    // Único valor de moneda documentado para Bre-B (XPAY-372/373) — no es
    // configurable: enviar cualquier otra currency sería construir un
    // request que Passport rechazaría, o peor, uno que Passport interprete
    // de forma no verificada. BrebPaymentRequestBuilder es el único
    // consumidor que la usa.
    public const string CopCurrency = "COP";

    private readonly IPassportHttpClient _http;

    public PassportPaymentClient(IPassportHttpClient http)
    {
        _http = http;
    }

    public async Task<PassportPaymentResponse> CreateBrebPaymentAsync(
        PassportCreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.AccountId))
            throw new ArgumentException("account_id es requerido.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ResolutionId))
            throw new ArgumentException("resolution_id es requerido.", nameof(request));
        if (request.Amount is null)
            throw new ArgumentException("amount es requerido.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Amount.Value))
            throw new ArgumentException("amount.value es requerido.", nameof(request));
        if (!string.Equals(request.Amount.Currency, CopCurrency, StringComparison.Ordinal))
            throw new ArgumentException($"amount.currency debe ser '{CopCurrency}'.", nameof(request));

        var response = await _http
            .PostAsync<PassportCreatePaymentRequest, PassportPaymentResponse>(
                CreateBrebPaymentPath, request, cancellationToken)
            .ConfigureAwait(false);

        return RequirePaymentId(response, "Create Bre-B Payment");
    }

    public async Task<PassportPaymentResponse> RetrievePaymentAsync(
        string paymentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
            throw new ArgumentException("payment_id es requerido.", nameof(paymentId));

        var path = string.Format(RetrievePaymentPathTemplate, Uri.EscapeDataString(paymentId));
        var response = await _http.GetAsync<PassportPaymentResponse>(path, cancellationToken).ConfigureAwait(false);

        return RequirePaymentId(response, "Retrieve Payment");
    }

    private static PassportPaymentResponse RequirePaymentId(PassportPaymentResponse? response, string operationName)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Id))
            throw new PassportProtocolException($"{operationName}: respuesta de Passport sin id (payment_id).");
        return response;
    }
}
