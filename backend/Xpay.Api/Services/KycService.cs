using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class KycService
{
    private readonly XpayDbContext       _db;
    private readonly IConfiguration      _config;
    private readonly IHttpClientFactory  _http;
    private readonly ILogger<KycService> _logger;

    private static readonly HashSet<string> EstadosValidos = new(StringComparer.Ordinal)
    {
        "NO_INICIADO", "PENDIENTE", "EN_REVISION", "APROBADO", "RECHAZADO", "EXPIRADO", "ERROR"
    };

    // Only QA demo wallet users are eligible for simulation — not XPAY staff accounts
    private static readonly HashSet<string> UsuariosQaPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "qa.usuario1", "qa.usuario2"
    };

    public KycService(
        XpayDbContext db,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<KycService> logger)
    {
        _db     = db;
        _config = config;
        _http   = httpClientFactory;
        _logger = logger;
    }

    public async Task<MiEstadoKycResponse> GetMiEstadoAsync(long idUsuario)
    {
        var datos = await _db.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new { u.EstadoKycActual, u.FechaKycActualizacion })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        string? sessionUrl = null;
        if (datos.EstadoKycActual == "PENDIENTE")
        {
            sessionUrl = await _db.KycVerificaciones.AsNoTracking()
                .Where(k => k.IdUsuario == idUsuario && k.EsActual && k.EstadoKyc == "PENDIENTE")
                .Select(k => k.SessionUrl)
                .FirstOrDefaultAsync();
        }

        return new MiEstadoKycResponse
        {
            EstadoKyc          = datos.EstadoKycActual,
            FechaActualizacion = datos.FechaKycActualizacion,
            SessionUrl         = sessionUrl,
            Nota               = "QA/Demo — sin verificación real de identidad en esta fase.",
        };
    }

    public async Task<IniciarKycResponse> CreateVeriffSessionAsync(long idUsuario)
    {
        // Read all three config values — validate presence, never log values
        var apiKey  = _config["VERIFF_API_KEY"];
        var baseUrl = _config["VERIFF_BASE_URL"];
        var secret  = _config["VERIFF_SHARED_SECRET"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("Veriff sandbox config incomplete for user {IdUsuario}.", idUsuario);
            throw new InvalidOperationException(
                "Veriff sandbox no configurado. Contacta al administrador.");
        }

        // No-PII vendor data — internal QA tracking only, no personal data sent to Veriff
        var vendorData = $"XPAY-QA-USUARIO-{idUsuario}";

        var payload = new
        {
            Verification = new
            {
                Callback    = "https://xpay-api-qa.azurewebsites.net/api/kyc/veriff/webhook",
                VendorData  = vendorData,
                Timestamp   = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                // RedirectUrl: redirect user back to XPAY after Veriff completion. Veriff V1 may ignore this field.
                RedirectUrl = "https://xpay-admin-qa.azurewebsites.net/mi-wallet?kyc=return",
            }
        };

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json     = JsonSerializer.Serialize(payload, jsonOpts);

        // X-HMAC-SIGNATURE: required by Veriff API — HMAC-SHA256(UTF-8 body, UTF-8 secret), hex lowercase
        var hmacKeyBytes  = Encoding.UTF8.GetBytes(secret.Trim());
        var hmacBodyBytes = Encoding.UTF8.GetBytes(json);
        using var hmacAlg = new HMACSHA256(hmacKeyBytes);
        var hmacHex       = Convert.ToHexString(hmacAlg.ComputeHash(hmacBodyBytes)).ToLowerInvariant();

        // Safe diagnostic: confirm both headers present and body/sig lengths — never log values
        _logger.LogInformation(
            "Veriff session request: hasAuthClient={HasAC} hasHmacSig={HasHS} bodyLen={BLen} sigLen={SLen}",
            !string.IsNullOrEmpty(apiKey),
            !string.IsNullOrEmpty(hmacHex),
            json.Length,
            hmacHex.Length);

        var client = _http.CreateClient();
        var req    = new HttpRequestMessage(
                         HttpMethod.Post,
                         $"{baseUrl.TrimEnd('/')}/v1/sessions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("X-AUTH-CLIENT",    apiKey);
        req.Headers.TryAddWithoutValidation("X-HMAC-SIGNATURE", hmacHex);

        HttpResponseMessage httpResp;
        try
        {
            httpResp = await client.SendAsync(req);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                "Veriff connection error for user {IdUsuario}: {Msg}", idUsuario, ex.Message);
            throw new InvalidOperationException(
                "Error de conexión con el proveedor de verificación. Intenta más tarde.");
        }

        var body = await httpResp.Content.ReadAsStringAsync();
        if (!httpResp.IsSuccessStatusCode)
        {
            // Extract Veriff error code/message for diagnostics — no secrets, no full body
            int?    veriffCode = null;
            string? veriffMsg  = null;
            try
            {
                var errEl = JsonSerializer.Deserialize<JsonElement>(body);
                if (errEl.TryGetProperty("code",    out var ec) && ec.ValueKind == JsonValueKind.Number)
                    veriffCode = ec.GetInt32();
                if (errEl.TryGetProperty("message", out var em))
                    veriffMsg = em.GetString();
            }
            catch { /* body not JSON — ignore */ }

            _logger.LogWarning(
                "Veriff returned HTTP {Status} for user {IdUsuario}. VeriffCode={VC} VeriffMsg={VM}",
                (int)httpResp.StatusCode, idUsuario, veriffCode, veriffMsg);
            throw new InvalidOperationException(
                $"El proveedor de verificación respondió con error {(int)httpResp.StatusCode}. Intenta más tarde.");
        }

        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(body); }
        catch
        {
            _logger.LogError("Veriff response unparseable for user {IdUsuario}.", idUsuario);
            throw new InvalidOperationException("Respuesta inesperada del proveedor de verificación.");
        }

        var statusVal  = root.TryGetProperty("status",       out var sv) ? sv.GetString()  : null;
        var hasVerif   = root.TryGetProperty("verification", out var vv);
        var sessionId  = hasVerif && vv.TryGetProperty("id",         out var sid) ? sid.GetString() : null;
        var sessionUrl = hasVerif && vv.TryGetProperty("url",        out var surl) ? surl.GetString() : null;
        var returnedVd = hasVerif && vv.TryGetProperty("vendorData", out var rvd) ? rvd.GetString() : null;

        if (statusVal != "success" || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(sessionUrl))
        {
            _logger.LogWarning(
                "Veriff unexpected response status '{Status}' for user {IdUsuario}.",
                statusVal, idUsuario);
            throw new InvalidOperationException("Respuesta inesperada del proveedor de verificación.");
        }

        // Deactivate previous KYC records for this user
        var anteriores = await _db.KycVerificaciones
            .Where(k => k.IdUsuario == idUsuario && k.EsActual)
            .ToListAsync();
        foreach (var a in anteriores)
        {
            a.EsActual           = false;
            a.FechaActualizacion = DateTime.UtcNow;
        }

        // Load user for idPersona + summary update
        var usuario = await _db.Usuarios
            .Where(u => u.IdUsuario == idUsuario)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        _db.KycVerificaciones.Add(new KycVerificacion
        {
            IdUsuario          = idUsuario,
            IdPersona          = usuario.IdPersona,
            Proveedor          = "VERIFF",
            EstadoKyc          = "PENDIENTE",
            SessionId          = sessionId,
            SessionUrl         = sessionUrl,
            VendorData         = returnedVd ?? vendorData,
            EsActual           = true,
            FechaCreacion      = DateTime.UtcNow,
            FechaActualizacion = DateTime.UtcNow,
        });

        usuario.EstadoKycActual       = "PENDIENTE";
        usuario.FechaKycActualizacion = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Veriff session created for user {IdUsuario}.", idUsuario);

        return new IniciarKycResponse
        {
            EstadoKyc  = "PENDIENTE",
            SessionId  = sessionId,
            SessionUrl = sessionUrl,
        };
    }

    // ── Veriff webhook signature validation ────────────────────────────────────
    // Header: x-hmac-signature (Veriff sends hex-encoded HMAC-SHA256 of raw body)
    // Algorithm: HMAC-SHA256(UTF-8 body bytes, UTF-8 VERIFF_SHARED_SECRET bytes)
    // Comparison: CryptographicOperations.FixedTimeEquals — constant-time, prevents timing attacks
    public bool ValidateVeriffSignature(string rawBody, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;

        var secret = _config["VERIFF_SHARED_SECRET"];
        if (string.IsNullOrWhiteSpace(secret)) return false;

        // Trim key to prevent Azure App Settings trailing-whitespace from causing mismatch
        var keyBytes  = Encoding.UTF8.GetBytes(secret.Trim());
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);

        using var hmac = new HMACSHA256(keyBytes);
        var computed   = hmac.ComputeHash(bodyBytes);
        var sigTrimmed = signature.Trim();

        // Safe diagnostic — format hints only, never reveals key or sig values
        _logger.LogInformation(
            "Webhook HMAC check: sigLen={SigLen} sigIsHex={IsHex} bodyLen={BodyLen}",
            sigTrimmed.Length,
            sigTrimmed.Length == 64 && sigTrimmed.All(c => "0123456789abcdefABCDEF".Contains(c)),
            rawBody.Length);

        // Try hex decode (Veriff standard)
        try
        {
            var sigBytes = Convert.FromHexString(sigTrimmed);
            if (CryptographicOperations.FixedTimeEquals(computed.AsSpan(), sigBytes.AsSpan()))
                return true;
        }
        catch { /* not valid hex */ }

        // Try base64 fallback (some Veriff webhook versions encode the signature in base64)
        try
        {
            var sigBytes = Convert.FromBase64String(sigTrimmed);
            if (CryptographicOperations.FixedTimeEquals(computed.AsSpan(), sigBytes.AsSpan()))
            {
                _logger.LogInformation("Webhook HMAC: matched via base64 decode.");
                return true;
            }
        }
        catch { /* not valid base64 */ }

        _logger.LogWarning("Webhook HMAC: signature mismatch. sigLen={SigLen}", sigTrimmed.Length);
        return false;
    }

    // ── Veriff webhook decision processing ─────────────────────────────────────
    // Called only after signature is validated.
    // Does NOT log raw body, PII, documents, or biometrics.
    // Logs: event received, sessionId, vendorData, mapped state, update result.
    public async Task<VeriffWebhookResult> ProcessVeriffWebhookAsync(string rawBody)
    {
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(rawBody); }
        catch
        {
            _logger.LogWarning("Veriff webhook: JSON parse failed.");
            return new VeriffWebhookResult { Processed = false };
        }

        // Part E: log top-level property names (not values) for structure diagnostics
        var topKeys = root.EnumerateObject().Select(p => p.Name);
        _logger.LogInformation("Veriff webhook top-level keys: [{Keys}]", string.Join(", ", topKeys));

        // Extract only the fields needed — never log person/document/image data
        var topStatus = root.TryGetProperty("status", out var sv) ? sv.GetString() : null;

        // Commit 4 — extracción de "verification" vía el parser puro
        // reutilizable (ParseVeriffDecision). root.status permanece fuera
        // del parser a propósito (ver comentario del parser): ese fallback
        // pertenece exclusivamente a este adaptador del webhook, nunca será
        // reutilizado tal cual por el futuro GET /decision, donde
        // root.status significa otra cosa (éxito de la llamada HTTP, no la
        // decisión KYC).
        root.TryGetProperty("verification", out var verification);
        var parsed = ParseVeriffDecision(verification);

        var sessionId  = parsed?.SessionId;
        var vendorData = parsed?.VendorData;
        // Prefer verification.status for decision; fall back to top-level status
        var decision   = parsed?.Decision ?? topStatus;
        var reason     = parsed?.Reason;

        _logger.LogInformation(
            "Veriff webhook received: topStatus={Status} sessionId={SessionId} vendorData={VendorData}",
            topStatus, sessionId, vendorData);

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogWarning("Veriff webhook: missing sessionId — cannot process.");
            return new VeriffWebhookResult { Processed = false };
        }

        // Map Veriff decision to XPAY internal state
        var estadoXpay = MapVeriffDecision(decision);

        if (estadoXpay == null)
        {
            // Non-decision events (started, submitted, etc.) — acknowledge, no state change
            _logger.LogInformation(
                "Veriff webhook: status '{Status}' is not a terminal/decision event — acknowledged, no state update.",
                topStatus);
            return new VeriffWebhookResult { Processed = false, SessionIdHint = sessionId };
        }

        // Find record by sessionId (prefer es_actual=true; fall back to most recent for this sessionId)
        var kyc = await _db.KycVerificaciones
            .Where(k => k.SessionId == sessionId && k.EsActual)
            .FirstOrDefaultAsync()
            ?? await _db.KycVerificaciones
                .Where(k => k.SessionId == sessionId)
                .OrderByDescending(k => k.IdKycVerificacion)
                .FirstOrDefaultAsync();

        if (kyc == null)
        {
            _logger.LogWarning(
                "Veriff webhook: sessionId '{SessionId}' not found in kyc_verificaciones — cannot update.",
                sessionId);
            return new VeriffWebhookResult { Processed = false, SessionIdHint = sessionId };
        }

        // ── Commit 4 — máquina de orden/idempotencia por attemptId/decisionTime ──
        // Basada exclusivamente en lo persistido en kyc_verificaciones
        // (migración 034), nunca en memoria del proceso. Solo compara —
        // nunca decide IdentidadVerificada ni toca Persona.
        var attemptIdPersistido      = kyc.AttemptIdVeriff;
        var attemptIdEntrante        = parsed?.AttemptId;
        var decisionTimePersistido   = kyc.DecisionTimeVeriff;
        var decisionTimeEntrante     = parsed?.DecisionTimeUtc;
        var debeActualizarSeguimientoIntento = false;

        if (attemptIdPersistido != null)
        {
            // Fila ya rastreada por 034 — aplicar la máquina de orden.
            if (attemptIdEntrante != null && attemptIdEntrante == attemptIdPersistido)
            {
                if (decision == kyc.Decision)
                {
                    // CASO A: reentrega idéntica — mismo attempt, misma decisión.
                    _logger.LogInformation(
                        "Veriff webhook: sessionId '{SessionId}' mismo attemptId y misma decisión ya registrada — idempotente.",
                        sessionId);
                    return new VeriffWebhookResult { Processed = true, EstadoMapeado = kyc.EstadoKyc, SessionIdHint = sessionId };
                }
                else
                {
                    // CASO B: mismo attempt, decisión distinta — anomalía, no se sobrescribe.
                    _logger.LogWarning(
                        "Veriff webhook: sessionId '{SessionId}' mismo attemptId con decisión distinta a la ya registrada — anomalía, no se sobrescribe.",
                        sessionId);
                    return new VeriffWebhookResult { Processed = true, EstadoMapeado = kyc.EstadoKyc, SessionIdHint = sessionId };
                }
            }
            else if (attemptIdEntrante != null)
            {
                // attemptId distinto — el orden se decide únicamente por decisionTime.
                if (decisionTimeEntrante.HasValue && decisionTimePersistido.HasValue)
                {
                    if (decisionTimeEntrante.Value > decisionTimePersistido.Value)
                    {
                        // CASO C: intento posterior legítimo — continúa el procesamiento normal.
                        debeActualizarSeguimientoIntento = true;
                    }
                    else
                    {
                        // CASO D: no es más reciente que el ya registrado — no se sobrescribe.
                        _logger.LogWarning(
                            "Veriff webhook: sessionId '{SessionId}' attemptId distinto pero decisionTime no es posterior al ya registrado — no se sobrescribe.",
                            sessionId);
                        return new VeriffWebhookResult { Processed = true, EstadoMapeado = kyc.EstadoKyc, SessionIdHint = sessionId };
                    }
                }
                else
                {
                    // CASO E: falta decisionTime en alguno de los dos lados — orden no determinable.
                    _logger.LogWarning(
                        "Veriff webhook: sessionId '{SessionId}' attemptId distinto pero no se puede determinar el orden (falta decisionTime) — no se sobrescribe.",
                        sessionId);
                    return new VeriffWebhookResult { Processed = true, EstadoMapeado = kyc.EstadoKyc, SessionIdHint = sessionId };
                }
            }
            else
            {
                // attemptId entrante ausente mientras ya existe uno rastreado — no se puede
                // comparar con certeza (mismo tratamiento conservador que el CASO E).
                _logger.LogWarning(
                    "Veriff webhook: sessionId '{SessionId}' sin attemptId entrante pero ya existe uno rastreado — orden no determinable, no se sobrescribe.",
                    sessionId);
                return new VeriffWebhookResult { Processed = true, EstadoMapeado = kyc.EstadoKyc, SessionIdHint = sessionId };
            }
        }
        else if (attemptIdEntrante != null || decisionTimeEntrante.HasValue)
        {
            // CASO E-TRANSICIÓN: fila histórica (anterior a 034), sin attemptId rastreado
            // todavía — se trata como el primer intento rastreado, sin bloquear. Se
            // procesa normalmente y se persisten los valores entrantes que sí vinieron.
            debeActualizarSeguimientoIntento = true;
        }

        // Idempotency: if already in this exact final state, skip without error
        var estadosFinales = new HashSet<string>(StringComparer.Ordinal)
            { "APROBADO", "RECHAZADO", "EXPIRADO", "ERROR" };

        if (estadosFinales.Contains(kyc.EstadoKyc) && kyc.EstadoKyc == estadoXpay && !debeActualizarSeguimientoIntento)
        {
            _logger.LogInformation(
                "Veriff webhook: sessionId '{SessionId}' already in final state '{Estado}' — idempotent skip.",
                sessionId, estadoXpay);
            return new VeriffWebhookResult { Processed = true, EstadoMapeado = estadoXpay, SessionIdHint = sessionId };
        }

        // Load user
        var usuario = await _db.Usuarios
            .Where(u => u.IdUsuario == kyc.IdUsuario)
            .FirstOrDefaultAsync();

        if (usuario == null)
        {
            _logger.LogWarning(
                "Veriff webhook: usuario {IdUsuario} not found for sessionId '{SessionId}'.",
                kyc.IdUsuario, sessionId);
            return new VeriffWebhookResult { Processed = false, SessionIdHint = sessionId };
        }

        // ── Commit 4 — consolidación de identidad Veriff → Persona (Casos 1-6) ──
        // Solo aplica cuando la decisión mapeada es APROBADO y el gate de 4
        // campos está completo (Casos 1, 3, 4, 5, 6 — requieren lock exclusivo
        // por documento). Si está incompleto, es el Caso 2 (sin lock, sin
        // IdentidadVerificada). Para cualquier otra decisión (RECHAZADO,
        // EN_REVISION, EXPIRADO, ERROR) este bloque se omite por completo y el
        // flujo cae, sin cambios, al escritor original de kyc/usuario de más abajo.
        if (estadoXpay == "APROBADO")
        {
            var gateCompleto =
                !string.IsNullOrWhiteSpace(parsed?.FirstName) &&
                !string.IsNullOrWhiteSpace(parsed?.LastName) &&
                !string.IsNullOrWhiteSpace(parsed?.DocumentType) &&
                !string.IsNullOrWhiteSpace(parsed?.DocumentNumber);

            if (!gateCompleto)
            {
                // CASO 2 — aprobado pero incompleto para el gate. Antes de
                // escribir cualquier campo crudo se verifica si la Persona ya
                // tenía la identidad consolidada: un payload incompleto NUNCA
                // debe sobrescribir parcialmente una identidad ya verificada.
                // Sin lock: no hay número de documento fiable para construir
                // la clave, y esta rama nunca marca IdentidadVerificada, por
                // lo que el índice único (que solo protege filas con
                // identidad_verificada = 1) no aplica.
                var personaIncompleta = await _db.Personas
                    .Where(p => p.IdPersona == usuario.IdPersona)
                    .FirstOrDefaultAsync();

                if (personaIncompleta == null)
                {
                    _logger.LogWarning(
                        "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} sin Persona asociada — no se puede persistir identidad incompleta.",
                        sessionId, usuario.IdUsuario);
                }
                else if (personaIncompleta.IdentidadVerificada)
                {
                    // Persona ya verificada + payload incompleto — no se toca
                    // ningún campo de identidad. kyc/usuario siguen su flujo
                    // normal más abajo.
                    _logger.LogInformation(
                        "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} caso identidad={Caso} — payload incompleto sobre Persona ya verificada, sin cambios.",
                        sessionId, usuario.IdUsuario, IdentidadCaso.Incompleta);
                }
                else
                {
                    var huboCambio = false;
                    if (!string.IsNullOrWhiteSpace(parsed?.FirstName))      { personaIncompleta.NombreVerificadoCompleto   = parsed!.FirstName;      huboCambio = true; }
                    if (!string.IsNullOrWhiteSpace(parsed?.LastName))       { personaIncompleta.ApellidoVerificadoCompleto = parsed!.LastName;       huboCambio = true; }
                    if (!string.IsNullOrWhiteSpace(parsed?.DocumentType))   { personaIncompleta.TipoDocumentoVeriffRaw     = parsed!.DocumentType;   huboCambio = true; }
                    if (!string.IsNullOrWhiteSpace(parsed?.DocumentNumber)) { personaIncompleta.NumeroDocumentoVerificado  = parsed!.DocumentNumber;  huboCambio = true; }
                    if (parsed?.DateOfBirth.HasValue == true)               { personaIncompleta.FechaNacimiento            = parsed.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue); huboCambio = true; }

                    if (huboCambio) personaIncompleta.FechaActualizacion = DateTime.UtcNow;

                    // CASO 2a — auditoría: por definición del gate incompleto
                    // siempre falta al menos uno de los 4 campos obligatorios.
                    // Solo se listan nombres técnicos de campo, nunca valores.
                    var camposFaltantes = new List<string>();
                    if (string.IsNullOrWhiteSpace(parsed?.FirstName))      camposFaltantes.Add("FIRST_NAME");
                    if (string.IsNullOrWhiteSpace(parsed?.LastName))       camposFaltantes.Add("LAST_NAME");
                    if (string.IsNullOrWhiteSpace(parsed?.DocumentType))   camposFaltantes.Add("DOCUMENT_TYPE");
                    if (string.IsNullOrWhiteSpace(parsed?.DocumentNumber)) camposFaltantes.Add("DOCUMENT_NUMBER");

                    _db.Auditorias.Add(new Auditoria
                    {
                        IdUsuario   = usuario.IdUsuario,
                        IdPersona   = personaIncompleta.IdPersona,
                        Modulo      = "KYC",
                        Accion      = "KYC_IDENTIDAD_INCOMPLETA",
                        Entidad     = "kyc_verificaciones",
                        IdEntidad   = kyc.IdKycVerificacion.ToString(),
                        Resultado   = "EXITOSO",
                        Observacion = $"Identidad no consolidada: campos obligatorios ausentes en el payload de Veriff: {string.Join(", ", camposFaltantes)}.",
                        FechaEvento = DateTime.UtcNow,
                    });

                    _logger.LogInformation(
                        "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} caso identidad={Caso} — gate incompleto, sin lock, sin IdentidadVerificada.",
                        sessionId, usuario.IdUsuario, IdentidadCaso.Incompleta);
                }

                // Cae al escritor original de kyc/usuario de más abajo (misma
                // transacción implícita de un solo SaveChangesAsync).
            }
            else
            {
                // Gate completo — requiere lock exclusivo por documento antes de
                // evaluar y, si aplica, escribir Persona (Casos 1, 3, 4, 5, 6).
                var idUnidadNegocioPersona = await _db.Personas.AsNoTracking()
                    .Where(p => p.IdPersona == usuario.IdPersona)
                    .Select(p => p.IdUnidadNegocio)
                    .FirstOrDefaultAsync();

                var documentoNormalizado = parsed!.DocumentNumber!.Trim().ToUpperInvariant();
                var claveLock = $"XPAY:IDENTIDAD_DOCUMENTO:{idUnidadNegocioPersona}:{documentoNormalizado}";

                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    var resultadoLock = await AppLockHelper.AdquirirAsync(_db, claveLock);
                    ValidarResultadoLockIdentidad(resultadoLock);

                    // Recarga rastreada de Persona DESPUÉS de adquirir el lock —
                    // ninguna lectura previa de su estado de identidad es de fiar.
                    var persona = await _db.Personas
                        .Where(p => p.IdPersona == usuario.IdPersona)
                        .FirstOrDefaultAsync();

                    if (persona == null)
                    {
                        _logger.LogWarning(
                            "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} sin Persona asociada — no se puede consolidar identidad.",
                            sessionId, usuario.IdUsuario);
                    }
                    else if (persona.IdentidadVerificada)
                    {
                        // Casos 4, 5, 6 — esta Persona ya estaba verificada. Nunca se
                        // sobrescribe: solo se clasifica para observabilidad.
                        // Comparación de documento con la misma normalización de la
                        // clave del lock (Trim().ToUpperInvariant()) — persona y
                        // parsed ya están materializados en memoria en este punto
                        // (persona recargada tras el lock, parsed ya parseado), por
                        // lo que ToUpperInvariant() aquí es C# puro, sin traducción
                        // a SQL.
                        var documentoExistenteNormalizado = persona.NumeroDocumentoVerificado?.Trim().ToUpperInvariant();
                        var mismoDocumento = documentoExistenteNormalizado == documentoNormalizado;

                        var mismoNombre   = string.Equals(persona.NombreVerificadoCompleto,   parsed.FirstName,    StringComparison.Ordinal);
                        var mismoApellido = string.Equals(persona.ApellidoVerificadoCompleto, parsed.LastName,     StringComparison.Ordinal);
                        var mismoTipoDoc  = string.Equals(persona.TipoDocumentoVeriffRaw,     parsed.DocumentType, StringComparison.Ordinal);
                        var mismosDatos   = mismoDocumento && mismoNombre && mismoApellido && mismoTipoDoc;

                        if (mismosDatos)
                        {
                            // CASO 4 — revalidación equivalente: sin cambios, sin auditoría.
                            _logger.LogInformation(
                                "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} caso identidad={Caso} — Persona ya verificada, sin cambios.",
                                sessionId, usuario.IdUsuario, IdentidadCaso.RevalidacionEquivalente);
                        }
                        else if (mismoDocumento)
                        {
                            // CASO 5 — mismo documento, otros datos distintos: no se
                            // sobrescribe. Solo etiquetas técnicas de campo, nunca valores.
                            var camposDistintos = new List<string>();
                            if (!mismoNombre)   camposDistintos.Add("NOMBRE");
                            if (!mismoApellido) camposDistintos.Add("APELLIDO");
                            if (!mismoTipoDoc)  camposDistintos.Add("TIPO_DOCUMENTO");

                            _db.Auditorias.Add(new Auditoria
                            {
                                IdUsuario   = usuario.IdUsuario,
                                IdPersona   = persona.IdPersona,
                                Modulo      = "KYC",
                                Accion      = "KYC_IDENTIDAD_CAMBIO_DATOS",
                                Entidad     = "kyc_verificaciones",
                                IdEntidad   = kyc.IdKycVerificacion.ToString(),
                                Resultado   = "EXITOSO",
                                Observacion = $"Identidad previamente verificada no modificada: Veriff entregó datos distintos para el mismo documento. Campos con diferencia: {string.Join(", ", camposDistintos)}.",
                                FechaEvento = DateTime.UtcNow,
                            });

                            _logger.LogInformation(
                                "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} caso identidad={Caso} — Persona ya verificada, sin cambios.",
                                sessionId, usuario.IdUsuario, IdentidadCaso.CambioDatos);
                        }
                        else
                        {
                            // CASO 6 — documento distinto al ya verificado: no se sobrescribe.
                            _db.Auditorias.Add(new Auditoria
                            {
                                IdUsuario   = usuario.IdUsuario,
                                IdPersona   = persona.IdPersona,
                                Modulo      = "KYC",
                                Accion      = "KYC_IDENTIDAD_CAMBIO_DOCUMENTO",
                                Entidad     = "kyc_verificaciones",
                                IdEntidad   = kyc.IdKycVerificacion.ToString(),
                                Resultado   = "EXITOSO",
                                Observacion = "Identidad previamente verificada no modificada: Veriff entregó un número de documento distinto al ya verificado para esta Persona.",
                                FechaEvento = DateTime.UtcNow,
                            });

                            _logger.LogInformation(
                                "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} caso identidad={Caso} — Persona ya verificada, sin cambios.",
                                sessionId, usuario.IdUsuario, IdentidadCaso.CambioDocumento);
                        }
                    }
                    else
                    {
                        // Persona aún no verificada — verificar conflicto de documento
                        // con otra Persona antes de escribir (bajo el lock exclusivo).
                        // Comparación normalizada sin ToUpperInvariant() dentro de la
                        // query (el proveedor SqlServer no la traduce): se usa
                        // Trim().ToUpper() sobre la columna (traducible a
                        // LTRIM(RTRIM())/UPPER() de SQL Server) contra
                        // documentoNormalizado, ya calculado en C# con
                        // Trim().ToUpperInvariant(). Para el rango de caracteres real
                        // de un número de documento (dígitos y letras A-Z), UPPER()
                        // de SQL Server y ToUpperInvariant() de .NET producen el
                        // mismo resultado, así que la comparación es equivalente a la
                        // normalización del lock sin depender del collation de la
                        // columna ni cargar Personas en memoria.
                        var conflicto = await _db.Personas.AnyAsync(p =>
                            p.IdPersona != persona.IdPersona &&
                            p.IdUnidadNegocio == persona.IdUnidadNegocio &&
                            p.NumeroDocumentoVerificado != null &&
                            p.NumeroDocumentoVerificado.Trim().ToUpper() == documentoNormalizado &&
                            p.IdentidadVerificada);

                        if (conflicto)
                        {
                            // CASO 3 — documento ya verificado en otra Persona: no se
                            // marca identidad, Persona no se toca. Nunca se registra el
                            // número de documento ni la identidad de la otra Persona.
                            _db.Auditorias.Add(new Auditoria
                            {
                                IdUsuario   = usuario.IdUsuario,
                                IdPersona   = persona.IdPersona,
                                Modulo      = "KYC",
                                Accion      = "KYC_IDENTIDAD_DOCUMENTO_DUPLICADO",
                                Entidad     = "kyc_verificaciones",
                                IdEntidad   = kyc.IdKycVerificacion.ToString(),
                                Resultado   = "EXITOSO",
                                Observacion = "Identidad no consolidada: el número de documento verificado ya está asociado a otra Persona activa en la misma unidad de negocio.",
                                FechaEvento = DateTime.UtcNow,
                            });

                            _logger.LogWarning(
                                "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} caso identidad={Caso} — número de documento verificado ya pertenece a otra Persona.",
                                sessionId, usuario.IdUsuario, IdentidadCaso.DocumentoDuplicado);
                        }
                        else
                        {
                            // CASO 1 — consolidación completa.
                            persona.NombreVerificadoCompleto     = parsed.FirstName;
                            persona.ApellidoVerificadoCompleto   = parsed.LastName;
                            persona.TipoDocumentoVeriffRaw       = parsed.DocumentType;
                            persona.NumeroDocumentoVerificado    = parsed.DocumentNumber;
                            if (parsed.DateOfBirth.HasValue) persona.FechaNacimiento = parsed.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue);
                            persona.IdentidadVerificada          = true;
                            persona.IdentidadVerificadaProveedor = "VERIFF";
                            persona.IdentidadVerificadaFecha     = DateTime.UtcNow;
                            persona.FechaActualizacion           = DateTime.UtcNow;

                            _db.Auditorias.Add(new Auditoria
                            {
                                IdUsuario     = usuario.IdUsuario,
                                IdPersona     = persona.IdPersona,
                                Modulo        = "KYC",
                                Accion        = "KYC_IDENTIDAD_CONSOLIDADA",
                                Entidad       = "kyc_verificaciones",
                                IdEntidad     = kyc.IdKycVerificacion.ToString(),
                                Resultado     = "EXITOSO",
                                ValorAnterior = "NO_VERIFICADA",
                                ValorNuevo    = "VERIFICADA",
                                Observacion   = "Identidad verificada y consolidada en Persona.",
                                FechaEvento   = DateTime.UtcNow,
                            });

                            _logger.LogInformation(
                                "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} caso identidad={Caso} — identidad consolidada.",
                                sessionId, usuario.IdUsuario, IdentidadCaso.Consolidada);
                        }
                    }

                    kyc.EstadoKyc          = estadoXpay;
                    kyc.Decision           = decision;
                    kyc.Reason             = reason;
                    kyc.FechaDecision      = DateTime.UtcNow;
                    kyc.FechaActualizacion = DateTime.UtcNow;
                    if (!string.IsNullOrWhiteSpace(vendorData)) kyc.VendorData = vendorData;
                    if (attemptIdEntrante != null) kyc.AttemptIdVeriff = attemptIdEntrante;
                    if (decisionTimeEntrante.HasValue) kyc.DecisionTimeVeriff = decisionTimeEntrante;
                    kyc.EsActual = true;

                    usuario.EstadoKycActual       = estadoXpay;
                    usuario.FechaKycActualizacion = DateTime.UtcNow;

                    try
                    {
                        await _db.SaveChangesAsync();
                    }
                    catch (DbUpdateException ex) when (EsViolacionDocumentoVerificadoDuplicado(ex))
                    {
                        // CASO 3 tardío — el índice único detectó, dentro de esta misma
                        // transacción, un conflicto que la consulta previa (bajo el
                        // mismo lock) no vio. Con el lock exclusivo por documento, esta
                        // ruta es estrictamente defensiva y no debería ocurrir en
                        // operación normal. No se reintenta con una segunda escritura:
                        // se descarta TODO este intento (incluyendo kyc/usuario) y se
                        // deja que una futura entrega del webhook, o una
                        // reconciliación, lo procese bajo el estado ya consistente de
                        // la base — mutar kyc/usuario en una segunda operación sin
                        // recargar todo el estado sería la escritura insegura que este
                        // diseño evita a propósito.
                        await tx.RollbackAsync();
                        _logger.LogWarning(
                            "Veriff webhook: sessionId '{SessionId}' idUsuario={IdUsuario} caso identidad={Caso} detectado tardíamente por el índice único — se descarta este intento completo.",
                            sessionId, usuario.IdUsuario, IdentidadCaso.DocumentoDuplicado);
                        return new VeriffWebhookResult { Processed = false, SessionIdHint = sessionId };
                    }

                    await tx.CommitAsync();

                    _logger.LogInformation(
                        "Veriff webhook processed: sessionId={SessionId} idUsuario={IdUsuario} estado={Estado}",
                        sessionId, kyc.IdUsuario, estadoXpay);

                    return new VeriffWebhookResult
                    {
                        Processed     = true,
                        EstadoMapeado = estadoXpay,
                        SessionIdHint = sessionId,
                    };
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }
        }

        // Transactional update of kyc record + user summary
        kyc.EstadoKyc          = estadoXpay;
        kyc.Decision           = decision;
        kyc.Reason             = reason;
        kyc.FechaDecision      = DateTime.UtcNow;
        kyc.FechaActualizacion = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(vendorData)) kyc.VendorData = vendorData;
        // Commit 4 — solo se rastrea lo que realmente vino en este webhook;
        // nunca se inventa ni se sobrescribe con null un valor ya rastreado.
        if (attemptIdEntrante != null) kyc.AttemptIdVeriff = attemptIdEntrante;
        if (decisionTimeEntrante.HasValue) kyc.DecisionTimeVeriff = decisionTimeEntrante;
        kyc.EsActual = true;

        usuario.EstadoKycActual       = estadoXpay;
        usuario.FechaKycActualizacion = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Veriff webhook processed: sessionId={SessionId} idUsuario={IdUsuario} estado={Estado}",
            sessionId, kyc.IdUsuario, estadoXpay);

        return new VeriffWebhookResult
        {
            Processed     = true,
            EstadoMapeado = estadoXpay,
            SessionIdHint = sessionId,
        };
    }

    // ── Commit 4 — clasificación interna (solo logging/estructura) de los
    // Casos 1-6 de consolidación de identidad. Nunca se expone vía
    // VeriffWebhookResult ni ningún DTO — puramente interna a este servicio.
    private enum IdentidadCaso
    {
        Consolidada,
        Incompleta,
        DocumentoDuplicado,
        RevalidacionEquivalente,
        CambioDatos,
        CambioDocumento,
    }

    // Solo traduce la violación de unicidad real de personas.UX_personas_documento_verificado
    // (documento ya verificado en otra Persona de la misma unidad de negocio) —
    // cualquier otra restricción única que coincida por casualidad con 2601/2627
    // se conserva como error inesperado, sin ocultarla bajo "documento duplicado".
    private static bool EsViolacionDocumentoVerificadoDuplicado(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx
        && (sqlEx.Number == 2601 || sqlEx.Number == 2627)
        && sqlEx.Message.Contains("UX_personas_documento_verificado", StringComparison.OrdinalIgnoreCase);

    private static string? MapVeriffDecision(string? decision) =>
        decision?.ToLowerInvariant() switch
        {
            "approved"               => "APROBADO",
            "declined"               => "RECHAZADO",
            "resubmission_requested" => "EN_REVISION",
            "review"                 => "EN_REVISION",
            "expired"                => "EXPIRADO",
            "abandoned"              => "EXPIRADO",
            "error"                  => "ERROR",
            _                        => null   // not a decision event — no state change
        };

    // ── Commit 4 — interpretación del resultado de AppLockHelper.AdquirirAsync
    // para la clave XPAY:IDENTIDAD_DOCUMENTO:{idUnidadNegocio}:{documento} ──
    // Deliberadamente NO usa AppLockHelper.ValidarResultado — esa lanza
    // OperacionCajaCierreConcurrenteException, cuyo tipo y mensaje
    // pertenecen semánticamente al dominio Caja/Cierre (Fase 70.4-B), no al
    // de identidad. No toca BD, no toca Persona, no crea Auditoria, no
    // recibe ni loguea el documento ni la clave del lock — solo interpreta
    // el código entero devuelto por sp_getapplock. Todavía NO conectado a
    // ProcessVeriffWebhookAsync (el bloque de identidad que lo invocará se
    // implementa en un paso posterior).
    private static void ValidarResultadoLockIdentidad(int resultado)
    {
        switch (resultado)
        {
            case 0:
            case 1:
                return;
            case -1:
                throw new IdentidadDocumentoConcurrenteException(
                    "Tiempo de espera agotado esperando otra verificación de identidad concurrente sobre este documento. Intenta de nuevo.");
            case -2:
                throw new IdentidadDocumentoConcurrenteException(
                    "La solicitud de sincronización de identidad fue cancelada. Intenta de nuevo.");
            case -3:
                throw new IdentidadDocumentoConcurrenteException(
                    "Se detectó un interbloqueo con otra verificación de identidad concurrente sobre este documento. Intenta de nuevo.");
            default:
                // Mismo criterio que el "default" de AppLockHelper.ValidarResultado:
                // error técnico de la llamada en sí (parámetros/infraestructura), no
                // contención legítima — se relanza sin tipar, sin incluir la clave
                // del lock ni ningún dato de documento.
                throw new Exception($"sp_getapplock devolvió un código inesperado: {resultado}.");
        }
    }

    // ── Commit 4 — parser puro de "verification" (Veriff) ───────────────────
    // Todavía NO conectado a ProcessVeriffWebhookAsync — el parseo inline
    // actual de ese método sigue intacto y en uso. Este parser existe para
    // ser reutilizado, sin cambios, tanto por el webhook (Commit 4) como por
    // el futuro fallback GET /v1/sessions/{sessionId}/decision (Commit 5) —
    // ambos comparten exactamente la misma forma documentada de
    // "verification". No consulta BD, no muta nada, no decide reglas de
    // negocio (IdentidadVerificada, mapeo TipoDocumento, Datacrédito, etc.):
    // solo extrae y normaliza técnicamente lo que el JSON trae. Sin ILogger,
    // sin DateTime.UtcNow, sin llamadas HTTP — completamente puro.
    //
    // root.status (nivel raíz, fuera de "verification") NUNCA se lee aquí a
    // propósito: en el webhook puede actuar como fallback de la decisión,
    // pero en GET /decision significa "la llamada HTTP tuvo éxito" — un dato
    // completamente distinto. Ese fallback, si aplica, permanece fuera de
    // este parser, en el adaptador de cada camino (webhook vs GET).
    private static VeriffDecisionParsed? ParseVeriffDecision(JsonElement verification)
    {
        if (verification.ValueKind != JsonValueKind.Object)
            return null;

        var hasPerson   = verification.TryGetProperty("person",   out var person)   && person.ValueKind   == JsonValueKind.Object;
        var hasDocument = verification.TryGetProperty("document", out var document) && document.ValueKind == JsonValueKind.Object;

        return new VeriffDecisionParsed(
            SessionId:       GetTrimmedString(verification, "id"),
            AttemptId:       GetTrimmedString(verification, "attemptId"),
            Decision:        GetTrimmedString(verification, "status"),
            Reason:          GetTrimmedString(verification, "reason"),
            DecisionTimeUtc: ParseVeriffUtcDateTime(verification, "decisionTime"),
            VendorData:      GetTrimmedString(verification, "vendorData"),
            FirstName:       hasPerson   ? GetTrimmedString(person, "firstName")     : null,
            LastName:        hasPerson   ? GetTrimmedString(person, "lastName")      : null,
            DocumentType:    hasDocument ? GetTrimmedString(document, "type")        : null,
            DocumentNumber:  hasDocument ? GetTrimmedString(document, "number")      : null,
            DateOfBirth:     hasPerson   ? ParseVeriffDateOnly(person, "dateOfBirth") : null
        );
    }

    // Regla única para todos los campos string del parser: ausente, JSON
    // null, tipo JSON inesperado (number/object/array/bool), o vacío tras
    // Trim() → null. Nunca lanza excepción por un tipo inesperado.
    private static string? GetTrimmedString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
            return null;

        var value = prop.GetString();
        if (string.IsNullOrEmpty(value)) return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    // verification.decisionTime — reloj de VERIFF (ISO 8601). Nunca debe
    // confundirse con KycVerificacion.FechaDecision (reloj de XPAY, asignado
    // con DateTime.UtcNow en ProcessVeriffWebhookAsync). Ausente/vacío/no
    // parseable → null, jamás un valor sustituto.
    private static DateTime? ParseVeriffUtcDateTime(JsonElement obj, string propertyName)
    {
        var raw = GetTrimmedString(obj, propertyName);
        if (raw is null) return null;

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    // verification.person.dateOfBirth — únicamente formato exacto
    // "yyyy-MM-dd", sin heurísticas de formato alternativo. Devuelve
    // DateOnly? — la conversión hacia Persona.FechaNacimiento (si aplica)
    // es responsabilidad explícita de la futura capa de persistencia, no de
    // este parser.
    private static DateOnly? ParseVeriffDateOnly(JsonElement obj, string propertyName)
    {
        var raw = GetTrimmedString(obj, propertyName);
        if (raw is null) return null;

        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    public async Task<string> SimularEstadoQaAsync(SimularEstadoKycRequest request)
    {
        var nombreUsuario = (request.Usuario ?? string.Empty).Trim().ToLower();

        if (!UsuariosQaPermitidos.Contains(nombreUsuario))
            throw new InvalidOperationException(
                $"Usuario '{request.Usuario}' no permitido para simulación. " +
                $"Usuarios QA válidos: {string.Join(", ", UsuariosQaPermitidos)}.");

        var estadoKyc = (request.EstadoKyc ?? string.Empty).Trim().ToUpper();
        if (!EstadosValidos.Contains(estadoKyc))
            throw new InvalidOperationException(
                $"EstadoKyc '{request.EstadoKyc}' inválido. " +
                $"Valores permitidos: {string.Join(", ", EstadosValidos)}.");

        var usuario = await _db.Usuarios
            .Where(u => u.NombreUsuario == nombreUsuario)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                $"Usuario '{request.Usuario}' no encontrado en la base de datos.");

        // Deactivate previous KYC records so only one is es_actual=true
        var anteriores = await _db.KycVerificaciones
            .Where(k => k.IdUsuario == usuario.IdUsuario && k.EsActual)
            .ToListAsync();
        foreach (var anterior in anteriores)
        {
            anterior.EsActual          = false;
            anterior.FechaActualizacion = DateTime.UtcNow;
        }

        _db.KycVerificaciones.Add(new KycVerificacion
        {
            IdUsuario          = usuario.IdUsuario,
            IdPersona          = usuario.IdPersona,
            Proveedor          = "SIMULACION_QA",
            EstadoKyc          = estadoKyc,
            EsActual           = true,
            FechaCreacion      = DateTime.UtcNow,
            FechaActualizacion = DateTime.UtcNow,
        });

        usuario.EstadoKycActual        = estadoKyc;
        usuario.FechaKycActualizacion  = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return $"Estado KYC de '{nombreUsuario}' actualizado a '{estadoKyc}' (SIMULACION_QA).";
    }
}
