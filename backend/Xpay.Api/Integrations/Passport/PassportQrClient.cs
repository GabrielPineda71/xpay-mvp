namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — implementación de IPassportQrClient sobre IPassportHttpClient
// (XPAY-272). Sin HttpClient propio, sin token provider propio: reutiliza
// íntegramente la infraestructura OAuth/HTTP existente.
//
// El path es una constante de contrato (confirmada documentalmente en
// XPAY-297/298) — NO es configurable por entorno, a diferencia de BaseUrl.
// La discrepancia guión/punto del host en la documentación EN queda fuera
// de esta implementación (XPAY-297/298): PASSPORT_BASE_URL sigue siendo la
// única fuente de verdad, vía IPassportHttpClient.
//
// Success HTTP: esta clase NO codifica ningún chequeo de status code
// específico. IPassportHttpClient ya acepta cualquier 2xx como éxito.
//
// Validación de input: guards de protocolo (evitar construir un request
// inutilizable) — ninguna regla de negocio no documentada. type/channel/
// vat_type/inc_type son enums C# tipados; transaction_purpose es un string
// validado contra el conjunto documentado (no puede ser un enum sin perder
// el cero inicial — ver PassportCreateQrCodeRequest). qr_code_reference
// sólo valida lo literalmente confirmado (máximo 17 caracteres, sin la
// letra 'P' mayúscula) — no se inventa una regla case-insensitive ni
// ninguna restricción adicional no documentada.
//
// Lifetime en DI: no registrado en Program.cs en esta fase (XPAY-298 es
// implementación offline sin conexión a ningún endpoint XPAY público nuevo).
public sealed class PassportQrClient : IPassportQrClient
{
    private const string CreateQrCodePath = "/v1/qrcodes";
    private const string DecodeQrCodePath = "/v1/qrcodes/decode";

    // Confirmado en XPAY-297/298: "00" Compras, "02" Anulaciones,
    // "03" Transferencias, "04" Retiro, "05" Recaudo, "06" Recargas,
    // "07" Depósito. StringComparer.Ordinal: preserva el cero inicial y no
    // acepta variantes (" 00", "0" solo, etc.) por coincidencia parcial.
    private static readonly HashSet<string> ValidTransactionPurposes =
        new(StringComparer.Ordinal) { "00", "02", "03", "04", "05", "06", "07" };

    private readonly IPassportHttpClient _http;

    public PassportQrClient(IPassportHttpClient http)
    {
        _http = http;
    }

    public async Task<PassportQrCodeResponse> CreateQrCodeAsync(
        PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var response = await _http
            .PostAsync<PassportCreateQrCodeRequest, PassportQrCodeResponse>(
                CreateQrCodePath, request, cancellationToken)
            .ConfigureAwait(false);

        return RequireQrId(response);
    }

    // XPAY-305 — Decode QR Code (POST /v1/qrcodes/decode), contrato
    // confirmado en XPAY-304. Reutiliza IPassportHttpClient.PostAsync sin
    // ningún cambio a la base HTTP/OAuth. Sin guard de protocolo tipo
    // RequireXxxId: XPAY-304 confirmó que Decode no documenta ningún campo
    // `id` a nivel raíz ni ningún otro campo como requerido de forma
    // exhaustiva en la respuesta — inventar un guard aquí violaría el
    // criterio evidence-first ya aplicado en el resto de la integración.
    public async Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
        PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDecodeRequest(request);

        var response = await _http
            .PostAsync<PassportDecodeQrCodeRequest, PassportDecodeQrCodeResponse>(
                DecodeQrCodePath, request, cancellationToken)
            .ConfigureAwait(false);

        // Transporte defensivo puro: a diferencia de CreateQrCodeAsync, no
        // hay un `id` remoto (ni ningún otro campo) documentado como
        // requerido de forma exhaustiva (XPAY-304) — un 2xx con body vacío
        // ("{}") es una respuesta válida a nivel de protocolo, no un error.
        // Sólo se rechaza null (fallo de deserialización total).
        if (response is null)
            throw new PassportProtocolException("Respuesta de Passport vacía (Decode QR Code).");

        return response;
    }

    // XPAY-305 — validación de presencia únicamente, sin inventar reglas no
    // documentadas: ni formato UUID para customer_id, ni longitud/regex
    // EMVCo para qr_code_data (XPAY-304 confirmó que la documentación no
    // exige ninguna de las dos).
    private static void ValidateDecodeRequest(PassportDecodeQrCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            throw new ArgumentException("customer_id es requerido.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.QrCodeData))
            throw new ArgumentException("qr_code_data es requerido.", nameof(request));
    }

    private static void Validate(PassportCreateQrCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.KeyId))
            throw new ArgumentException("key_id es requerido.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            throw new ArgumentException("customer_id es requerido.", nameof(request));

        // XPAY-289/298 — mismo hallazgo corregido en Create Key: un enum
        // construido fuera de rango (cast explícito) no es rechazado por el
        // compilador ni por JsonStringEnumConverter al serializar (se
        // convertiría silenciosamente en un número JSON crudo). Enum.IsDefined
        // cierra ese hueco ANTES de HTTP para los 3 enums de este request.
        if (!Enum.IsDefined(request.Type))
            throw new ArgumentException("type no es un valor válido.", nameof(request));
        if (!Enum.IsDefined(request.Channel))
            throw new ArgumentException("channel no es un valor válido.", nameof(request));

        if (request.AdditionalInfo is null)
            throw new ArgumentException("additional_info es requerido.", nameof(request));
        if (!ValidTransactionPurposes.Contains(request.AdditionalInfo.TransactionPurpose))
            throw new ArgumentException("additional_info.transaction_purpose no es un valor válido.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AdditionalInfo.TerminalLabel))
            throw new ArgumentException("additional_info.terminal_label es requerido.", nameof(request));
        if (request.AdditionalInfo.TerminalLabel.Length > 25)
            throw new ArgumentException("additional_info.terminal_label excede 25 caracteres.", nameof(request));

        // XPAY-357 — vat dejó de ser incondicionalmente requerido: la
        // documentación oficial vigente revisada por el director muestra
        // el ejemplo STATIC SIN vat. Se preserva sin cambios el
        // comportamiento DYNAMIC previo (vat seguía siendo requerido para
        // DYNAMIC) — sólo STATIC deja de exigirlo. Cuando vat SÍ está
        // presente (en cualquiera de los dos tipos), sus subcampos se
        // validan exactamente igual que antes — mismo criterio ya aplicado
        // a amount/inc (validación condicional a la presencia, no al tipo).
        if (request.Type == PassportQrType.DYNAMIC && request.Vat is null)
            throw new ArgumentException("vat es requerido cuando type es DYNAMIC.", nameof(request));

        if (request.Vat is not null)
        {
            if (!Enum.IsDefined(request.Vat.VatType))
                throw new ArgumentException("vat.vat_type no es un valor válido.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.Vat.VatValue))
                throw new ArgumentException("vat.vat_value es requerido.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.Vat.VatBaseValue))
                throw new ArgumentException("vat.vat_base_value es requerido.", nameof(request));
        }

        if (request.Amount is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Amount.Value))
                throw new ArgumentException("amount.value es requerido cuando amount está presente.", nameof(request));
            if (request.Amount.Currency != "COP")
                throw new ArgumentException("amount.currency debe ser \"COP\".", nameof(request));
        }

        if (request.Inc is not null)
        {
            if (!Enum.IsDefined(request.Inc.IncType))
                throw new ArgumentException("inc.inc_type no es un valor válido.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.Inc.IncValue))
                throw new ArgumentException("inc.inc_value es requerido cuando inc está presente.", nameof(request));
        }

        // XPAY-300 (hallazgo XPAY-299 FINDING_1) — Passport documenta
        // verbatim: "Required for Dynamic QR Codes if an Amount is
        // provided". Se aplica ÚNICAMENTE a DYNAMIC + amount presente — no
        // convierte a DYNAMIC en "amount obligatorio" (DYNAMIC sin amount
        // sigue permitido) ni a "inc" en "amount obligatorio" (inc sin
        // amount NO se bloquea artificialmente: Passport no documenta esa
        // dirección inversa). STATIC no se ve afectado en absoluto —el
        // guard sólo evalúa cuando Type == DYNAMIC.
        if (request.Type == PassportQrType.DYNAMIC && request.Amount is not null && request.Inc is null)
            throw new ArgumentException("inc es requerido cuando amount está presente en un QR DYNAMIC.", nameof(request));

        if (request.QrCodeReference is not null)
        {
            if (request.QrCodeReference.Length == 0)
                throw new ArgumentException("qr_code_reference no puede ser una cadena vacía.", nameof(request));
            if (request.QrCodeReference.Length > 17)
                throw new ArgumentException("qr_code_reference excede 17 caracteres.", nameof(request));
            // Literalmente confirmado: "Maximum of 17 alphanumeric
            // characters" — char.IsLetterOrDigit (XPAY-300, hallazgo
            // XPAY-299 FINDING_2). Verificado empíricamente que rechaza
            // guiones/espacios/símbolos correctamente, pero acepta letras
            // Unicode (ej. 'Ñ', CJK) además de ASCII — la documentación
            // dice sólo "alphanumeric" sin especificar ASCII vs Unicode, así
            // que NO se endurece a [A-Za-z0-9] sin evidencia de que Passport
            // lo exija; se deja la decisión explícita y documentada aquí.
            if (!request.QrCodeReference.All(char.IsLetterOrDigit))
                throw new ArgumentException("qr_code_reference debe ser alfanumérico.", nameof(request));
            // Literalmente confirmado: "The letter P is not permitted" — se
            // aplica sólo a la letra 'P' mayúscula tal como está escrita en
            // la documentación; no se infiere una regla case-insensitive
            // no confirmada (XPAY-298 Fase 8, reconfirmado XPAY-300).
            if (request.QrCodeReference.Contains('P'))
                throw new ArgumentException("qr_code_reference no puede contener la letra 'P'.", nameof(request));
        }
    }

    // XPAY-298 — mismo patrón que RequireCustomerId/RequireAccountId/
    // RequireKeyId de fases anteriores: una respuesta 2xx correctamente
    // deserializada puede aun así carecer del `id` remoto (ausente, "" o
    // whitespace). Se reutiliza PassportProtocolException — no se crea una
    // familia nueva. Mensaje estático y saneado: nunca incluye body, token,
    // Authorization, key_id, customer_id, qr_code_reference ni PII.
    private static PassportQrCodeResponse RequireQrId(PassportQrCodeResponse? response)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Id))
            throw new PassportProtocolException("Respuesta de Passport sin qr id (Create QR Code).");
        return response;
    }
}
