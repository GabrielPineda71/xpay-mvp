namespace Xpay.Api.Integrations.Passport;

// XPAY-287 — implementación de IPassportKeyClient sobre IPassportHttpClient
// (XPAY-272). Sin HttpClient propio, sin token provider propio: reutiliza
// íntegramente la infraestructura OAuth/HTTP existente.
//
// El path es una constante de contrato (confirmada documentalmente en
// XPAY-286) — NO es configurable por entorno, a diferencia de BaseUrl.
//
// Success HTTP: esta clase NO codifica ningún chequeo de status code
// específico. IPassportHttpClient ya acepta cualquier 2xx como éxito
// (HttpResponseMessage.IsSuccessStatusCode) — el 200 OK confirmado
// documentalmente para Create Key ya queda cubierto sin lógica adicional.
//
// Validación de input: sólo guards obvios de protocolo (evitar construir un
// request inutilizable) — ninguna regla de negocio no documentada. key_type
// es un enum C# tipado (PassportKeyType): el compilador ya garantiza que
// sólo puede ser uno de ID/PHONE/EMAIL/ALPHA/BCODE, por lo que no se agrega
// una validación de runtime redundante para ese campo.
//
// Lifetime en DI: no registrado en Program.cs en esta fase (XPAY-287 es
// implementación offline sin conexión a ningún endpoint XPAY público nuevo).
public sealed class PassportKeyClient : IPassportKeyClient
{
    private const string CreateKeyPath = "/v1/keys";

    private readonly IPassportHttpClient _http;

    public PassportKeyClient(IPassportHttpClient http)
    {
        _http = http;
    }

    public async Task<PassportKeyResponse> CreateKeyAsync(
        PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.AccountId))
            throw new ArgumentException("account_id es requerido.", nameof(request));
        if (request.Key is null)
            throw new ArgumentException("key es requerido.", nameof(request));
        // XPAY-289 (hallazgo XPAY-288): un PassportKeyType construido fuera de
        // los valores declarados (p.ej. un cast explícito como (PassportKeyType)999)
        // NO es rechazado por el compilador ni por JsonStringEnumConverter — este
        // último, verificado empíricamente, serializa ese caso como un NÚMERO JSON
        // crudo ("key_type":999) en vez de fallar, lo que enviaría un request
        // malformado a Passport. Enum.IsDefined cierra ese hueco ANTES de
        // serializar/hacer HTTP, con el mismo patrón de guard de protocolo
        // (ArgumentException) ya usado para account_id/key/key_value en este
        // mismo método.
        if (!Enum.IsDefined(request.Key.KeyType))
            throw new ArgumentException("key_type no es un valor válido.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Key.KeyValue))
            throw new ArgumentException("key_value es requerido.", nameof(request));

        var response = await _http
            .PostAsync<PassportCreateKeyRequest, PassportKeyResponse>(
                CreateKeyPath, request, cancellationToken)
            .ConfigureAwait(false);

        return RequireKeyId(response);
    }

    // XPAY-287 — mismo patrón que RequireCustomerId/RequireAccountId
    // (PassportCustomerAccountClient, XPAY-281): una respuesta 2xx
    // correctamente deserializada puede aun así carecer del `id` remoto
    // (ausente, "" o whitespace). Se reutiliza PassportProtocolException —
    // no se crea una familia nueva. Mensaje estático y saneado: nunca
    // incluye body, token, Authorization, account_id, key_value ni PII.
    private static PassportKeyResponse RequireKeyId(PassportKeyResponse? response)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Id))
            throw new PassportProtocolException("Respuesta de Passport sin key id (Create Key).");
        return response;
    }
}
