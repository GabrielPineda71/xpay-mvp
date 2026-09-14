namespace Xpay.Api.Integrations.Passport;

// XPAY-279 — implementación de IPassportCustomerAccountClient sobre
// IPassportHttpClient (XPAY-272). Sin HttpClient propio, sin token provider
// propio: reutiliza íntegramente la infraestructura OAuth/HTTP existente —
// el Bearer token, el fail-closed de configuración, el saneamiento de logs
// y el manejo de errores 401/403/no-success ya viven en PassportHttpClient.
//
// Los paths son constantes de contrato (confirmadas documentalmente en
// XPAY-278) — NO son configurables por entorno, a diferencia de BaseUrl.
//
// Success HTTP: esta clase NO codifica ningún chequeo de status code
// específico (200 vs 201). IPassportHttpClient ya acepta cualquier 2xx como
// éxito (HttpResponseMessage.IsSuccessStatusCode) y sólo distingue
// 401/403 (auth) de cualquier otro no-2xx (transporte) — por diseño genérico
// ya construido en XPAY-272. La discrepancia 200/201 entre el anexo de
// certificación y la documentación técnica oficial (ver XPAY-278 Fase 7)
// por tanto NO requiere ningún workaround aquí: ambos códigos ya son
// aceptados como éxito sin lógica adicional.
//
// customer_id/account_id se incorporan al path vía Uri.EscapeDataString
// para evitar construir una URI inválida o inyectar segmentos de path no
// intencionados.
//
// Lifetime en DI: no registrado todavía en Program.cs en esta fase (XPAY-279
// es implementación offline sin conexión a ningún endpoint XPAY público
// nuevo) — el registro DI, si se decide, queda para una fase posterior.
public sealed class PassportCustomerAccountClient : IPassportCustomerAccountClient
{
    private const string LinkMerchantPath = "/v1/customers/business/link";
    private const string LinkAccountPath  = "/v1/accounts/link";

    private readonly IPassportHttpClient _http;

    public PassportCustomerAccountClient(IPassportHttpClient http)
    {
        _http = http;
    }

    public async Task<PassportCustomerResponse> LinkMerchantAsync(
        PassportLinkMerchantRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _http
            .PostAsync<PassportLinkMerchantRequest, PassportCustomerResponse>(
                LinkMerchantPath, request, cancellationToken)
            .ConfigureAwait(false);

        return RequireCustomerId(response, "Link Merchant");
    }

    public async Task<PassportCustomerResponse> RetrieveCustomerAsync(
        string customerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("customerId es requerido.", nameof(customerId));

        var path = $"/v1/customers/{Uri.EscapeDataString(customerId)}";

        var response = await _http
            .GetAsync<PassportCustomerResponse>(path, cancellationToken)
            .ConfigureAwait(false);

        return RequireCustomerId(response, "Retrieve Customer");
    }

    public async Task<PassportAccountResponse> LinkAccountAsync(
        PassportLinkAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _http
            .PostAsync<PassportLinkAccountRequest, PassportAccountResponse>(
                LinkAccountPath, request, cancellationToken)
            .ConfigureAwait(false);

        return RequireAccountId(response, "Link Account");
    }

    public async Task<PassportAccountResponse> RetrieveAccountAsync(
        string accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("accountId es requerido.", nameof(accountId));

        var path = $"/v1/accounts/{Uri.EscapeDataString(accountId)}";

        var response = await _http
            .GetAsync<PassportAccountResponse>(path, cancellationToken)
            .ConfigureAwait(false);

        return RequireAccountId(response, "Retrieve Account");
    }

    // XPAY-281 (hallazgo XPAY-280 #2) — una respuesta 2xx correctamente
    // deserializada (objeto no null) puede aun así carecer del `id` remoto
    // (ausente, "" o sólo whitespace: `{}`, `"id":""`, `"id":"   "` son JSON
    // igualmente válidos). El propósito único de estas operaciones es
    // obtener un customer_id/account_id utilizable en pasos posteriores
    // (Link Account requiere customer_id; una Bre-B Key requiere account_id)
    // — por eso esta ausencia se trata como un fallo de PROTOCOLO explícito
    // en vez de devolverse silenciosamente como "éxito" con un id inutilizable.
    // Se reutiliza PassportProtocolException (ya existente) — no se crea una
    // familia nueva. El mensaje es estático y saneado: nunca incluye el body,
    // el token, el header Authorization ni datos de identificación del request.
    private static PassportCustomerResponse RequireCustomerId(PassportCustomerResponse? response, string operationName)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Id))
            throw new PassportProtocolException($"Respuesta de Passport sin customer id ({operationName}).");
        return response;
    }

    private static PassportAccountResponse RequireAccountId(PassportAccountResponse? response, string operationName)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Id))
            throw new PassportProtocolException($"Respuesta de Passport sin account id ({operationName}).");
        return response;
    }
}
