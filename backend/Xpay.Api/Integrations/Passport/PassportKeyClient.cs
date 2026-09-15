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
    private const string ResolveKeyPath = "/v1/resolve-key";
    // XPAY-328 — misma ruta base que CreateKeyPath ("/v1/keys"): es la
    // colección del mismo recurso, GET en vez de POST. Se declara como
    // constante separada para que el nombre en el sitio de la llamada
    // (ListKeysAsync) sea auto-explicativo, sin renombrar CreateKeyPath.
    private const string ListKeysPath = "/v1/keys";

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

    // XPAY-293 — PATCH /v1/keys/{key_id}/suspend, sin request body (contrato
    // confirmado en XPAY-292). Reutiliza PassportKeyResponse: el shape
    // confirmado (created_at, updated_at, account_id, id, status,
    // key{key_type,key_value}) es un subconjunto exacto de las propiedades
    // ya modeladas — DisplayName simplemente queda null si el proveedor no
    // lo incluye en esta respuesta, sin romper nada (transporte defensivo
    // ya establecido para este DTO).
    public async Task<PassportKeyResponse> SuspendKeyAsync(
        string keyId, CancellationToken cancellationToken = default)
    {
        ValidateKeyId(keyId);
        var path = $"/v1/keys/{Uri.EscapeDataString(keyId)}/suspend";

        var response = await _http
            .PatchAsync<PassportKeyResponse>(path, cancellationToken)
            .ConfigureAwait(false);

        return RequireKeyId(response, "Suspend Key");
    }

    // XPAY-293 — PATCH /v1/keys/{key_id}/activate, sin request body.
    // Passport nombra esta operación "Activate Key" (no "Reactivate").
    public async Task<PassportKeyResponse> ActivateKeyAsync(
        string keyId, CancellationToken cancellationToken = default)
    {
        ValidateKeyId(keyId);
        var path = $"/v1/keys/{Uri.EscapeDataString(keyId)}/activate";

        var response = await _http
            .PatchAsync<PassportKeyResponse>(path, cancellationToken)
            .ConfigureAwait(false);

        return RequireKeyId(response, "Activate Key");
    }

    // XPAY-293 — DELETE /v1/keys/{key_id}. Éxito documentado = 204 No
    // Content sin body — IPassportHttpClient.DeleteAsync nunca intenta
    // deserializar nada, así que no hay `id` que exigir aquí: la ausencia de
    // excepción ES la confirmación de éxito.
    public async Task DeleteKeyAsync(
        string keyId, CancellationToken cancellationToken = default)
    {
        ValidateKeyId(keyId);
        var path = $"/v1/keys/{Uri.EscapeDataString(keyId)}";

        await _http.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
    }

    // XPAY-293 — guard de protocolo compartido por Suspend/Activate/Delete:
    // mismo criterio ya usado para account_id/key/key_value en CreateKeyAsync
    // (ArgumentException, mensaje corto y saneado, sin PII).
    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("key_id es requerido.", nameof(keyId));
    }

    // XPAY-324 — Resolve Key (POST /v1/resolve-key), contrato confirmado en
    // XPAY-310. Reutiliza IPassportHttpClient.PostAsync sin ningún cambio a
    // la base HTTP/OAuth.
    public async Task<PassportResolveKeyResponse> ResolveKeyAsync(
        PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            throw new ArgumentException("customer_id es requerido.", nameof(request));
        if (request.Key is null)
            throw new ArgumentException("key es requerido.", nameof(request));
        // XPAY-289/324 — mismo hallazgo corregido en Create Key: un enum
        // construido fuera de rango no es rechazado por el compilador ni por
        // JsonStringEnumConverter al serializar. Enum.IsDefined cierra ese
        // hueco ANTES de HTTP.
        if (!Enum.IsDefined(request.Key.KeyType))
            throw new ArgumentException("key_type no es un valor válido.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Key.KeyValue))
            throw new ArgumentException("key_value es requerido.", nameof(request));

        var response = await _http
            .PostAsync<PassportResolveKeyRequest, PassportResolveKeyResponse>(
                ResolveKeyPath, request, cancellationToken)
            .ConfigureAwait(false);

        return RequireResolutionId(response);
    }

    // XPAY-328 — List Keys (GET /v1/keys), contrato confirmado en XPAY-327.
    // Read-only: no muta ningún recurso, no requiere confirm-flag alguna a
    // nivel de negocio. Filtro EXACTO por account_id+key_type+key_value —
    // los tres simultáneamente, construidos en el query string con
    // Uri.EscapeDataString en cada valor (nunca concatenación insegura;
    // relevante incluso hoy con BCODE numérico, porque otros key_type
    // futuros como EMAIL/ALPHA pueden contener '@', '+' u otros caracteres
    // reservados de URL).
    //
    // A diferencia de CreateKeyAsync/SuspendKeyAsync/ResolveKeyAsync, una
    // respuesta con `keys: []` NO es un fallo de protocolo — es un
    // resultado legítimo ("ninguna llave coincide con el filtro"), por lo
    // que aquí NO se aplica el patrón RequireKeyId/RequireResolutionId.
    public async Task<PassportListKeysResponse> ListKeysAsync(
        string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("account_id es requerido.", nameof(accountId));
        // XPAY-289/324/328 — mismo hallazgo recurrente: un PassportKeyType
        // construido fuera de rango (cast explícito) no es rechazado por el
        // compilador. Enum.IsDefined cierra ese hueco ANTES de construir el
        // query string / hacer HTTP.
        if (!Enum.IsDefined(keyType))
            throw new ArgumentException("key_type no es un valor válido.", nameof(keyType));
        if (string.IsNullOrWhiteSpace(keyValue))
            throw new ArgumentException("key_value es requerido.", nameof(keyValue));

        var query = string.Join('&',
            $"account_id={Uri.EscapeDataString(accountId)}",
            $"key_type={Uri.EscapeDataString(keyType.ToString())}",
            $"key_value={Uri.EscapeDataString(keyValue)}");
        var path = $"{ListKeysPath}?{query}";

        var response = await _http
            .GetAsync<PassportListKeysResponse>(path, cancellationToken)
            .ConfigureAwait(false);

        // GetAsync devuelve default (null) cuando el body está vacío
        // (ContentLength=0) — se normaliza a una lista vacía en vez de
        // propagar null, para que el llamador nunca tenga que null-check
        // dos formas distintas de "cero resultados".
        return response ?? new PassportListKeysResponse();
    }

    // XPAY-324 — mismo patrón que RequireKeyId/RequireCustomerId/
    // RequireAccountId: una respuesta 2xx correctamente deserializada puede
    // aun así carecer del `id` remoto (ausente, "" o whitespace). Aquí `id`
    // es el resolution_id, que debe reutilizarse en el retry de un payment
    // como idempotency key (XPAY-310) — una respuesta sin él es inutilizable
    // para su propósito, por lo que se trata como fallo de protocolo
    // explícito. Mensaje estático y saneado: nunca incluye body, token,
    // Authorization, customer_id, key_value ni datos de owner/participant/
    // account.
    private static PassportResolveKeyResponse RequireResolutionId(PassportResolveKeyResponse? response)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Id))
            throw new PassportProtocolException("Respuesta de Passport sin resolution id (Resolve Key).");
        return response;
    }

    // XPAY-287/293 — mismo patrón que RequireCustomerId/RequireAccountId
    // (PassportCustomerAccountClient, XPAY-281): una respuesta 2xx
    // correctamente deserializada puede aun así carecer del `id` remoto
    // (ausente, "" o whitespace). Se reutiliza PassportProtocolException —
    // no se crea una familia nueva. Mensaje estático y saneado: nunca
    // incluye body, token, Authorization, account_id, key_value ni PII.
    private static PassportKeyResponse RequireKeyId(PassportKeyResponse? response, string operationName = "Create Key")
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Id))
            throw new PassportProtocolException($"Respuesta de Passport sin key id ({operationName}).");
        return response;
    }
}
