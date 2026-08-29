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

// ── Commit 5 — resultado del orquestador de reconciliación administrativa
// (KycService.ReconciliarVeriffAsync). Internal (no público, no DTO): solo
// consumido por KycController dentro del mismo ensamblado. Nunca transporta
// sessionId, nombres, documentos, vendorData, raw JSON, HMAC ni secretos —
// únicamente categoría técnica + el estado XPAY ya mapeado + un mensaje
// corto ya saneado.
internal enum ReconciliacionVeriffCategoria
{
    FilaNoEncontrada,
    NoElegible,
    SinDecisionTodavia,
    ProcesadoConCambios,
    ProcesadoSinCambiosPorIdempotencia,
    SesionNoEncontradaEnVeriff,
    ErrorDefinitivoVeriff,
    ErrorTransitorioVeriff,
    ErrorTecnicoInesperado,
}

internal sealed record ReconciliacionVeriffResultado(
    ReconciliacionVeriffCategoria Categoria,
    string?                       EstadoMapeado,
    string?                       Mensaje
);

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

        // ── Commit 5 — a partir de aquí ya hay una sesión Veriff válida; toda
        // interacción con Veriff terminó arriba. Todo lo que sigue es la fase
        // DB, serializada por XPAY:KYC_USUARIO:{idUsuario} — CreateVeriffSessionAsync
        // es el único dueño de decidir cuál sesión KYC del usuario queda
        // vigente (EsActual). La consulta de sesiones "anteriores" y la carga
        // de Usuario se ejecutan DENTRO de la transacción y DESPUÉS de
        // adquirir el lock — nunca se reutiliza un listado leído antes —
        // para que dos invocaciones concurrentes de este método para el
        // mismo usuario queden correctamente serializadas: la segunda en
        // obtener el lock relee el estado ya comprometido por la primera y
        // desactiva la sesión que esta acababa de crear, dejando siempre
        // exactamente una fila EsActual=true. No se intenta cancelar ninguna
        // sesión Veriff huérfana — es una consecuencia aceptada del diseño.
        var claveLockUsuario = $"XPAY:KYC_USUARIO:{idUsuario}";

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var resultadoLockUsuario = await AppLockHelper.AdquirirAsync(_db, claveLockUsuario);
            ValidarResultadoLockKycUsuario(resultadoLockUsuario);

            // Load user for idPersona + summary update — dentro del lock.
            var usuario = await _db.Usuarios
                .Where(u => u.IdUsuario == idUsuario)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Usuario no encontrado.");

            // Deactivate previous KYC records for this user — recargadas
            // DENTRO del lock, nunca desde un listado leído antes de adquirirlo.
            var anteriores = await _db.KycVerificaciones
                .Where(k => k.IdUsuario == idUsuario && k.EsActual)
                .ToListAsync();
            foreach (var a in anteriores)
            {
                a.EsActual           = false;
                a.FechaActualizacion = DateTime.UtcNow;
            }

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
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

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

    // ── Commit 5 — resultado interno puro de consultar GET /v1/sessions/
    // {id}/decision. Solo uso interno de FetchVeriffDecisionAsync — sin DTO
    // público todavía. Exito=true cubre tanto "verification disponible" como
    // "verification=null" (SinDecision=true distingue ambos). Exito=false +
    // ErrorTransitorio distingue errores definitivos (config/4xx/contrato
    // inesperado) de transitorios (5xx/timeout/fallo de transporte) para que
    // el futuro orquestador decida si reintentar. Nunca transporta raw JSON,
    // API key, HMAC, shared secret ni sessionId — Decision (cuando existe)
    // ya pasó por ParseVeriffDecision, el mismo parser puro del webhook.
    private sealed record VeriffDecisionFetchResult(
        bool                   Exito,
        bool                   SinDecision,
        bool                   ErrorTransitorio,
        int?                   HttpStatus,
        VeriffDecisionParsed?  Decision,
        string?                Error
    );

    // ── Commit 5 — cliente HTTP puro de GET /v1/sessions/{id}/decision.
    // Completamente desconectado del flujo funcional en este sub-paso: no
    // invocado desde ningún otro método todavía. No toca _db, no abre
    // transacción, no adquiere ningún AppLock, no decide elegibilidad, no
    // carga ninguna fila KYC — recibe un sessionId y consulta Veriff, nada
    // más. La interpretación de negocio del resultado (MapVeriffDecision,
    // máquina A-E, Casos 1-6) queda exclusivamente en
    // ProcesarDecisionVeriffAsync, invocado por un futuro orquestador.
    //
    // root.status de este endpoint significa ÚNICAMENTE que la llamada HTTP
    // tuvo éxito — nunca la decisión KYC (a diferencia del webhook, donde
    // root.status sí puede actuar como fallback). Por eso este método jamás
    // hace `parsed.Decision ?? rootStatus`: root.status ni siquiera se
    // expone en VeriffDecisionFetchResult, solo se usa para validar que la
    // llamada fue exitosa.
    private async Task<VeriffDecisionFetchResult> FetchVeriffDecisionAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return new VeriffDecisionFetchResult(false, false, false, null, null, "sessionId vacío.");

        // Read all three config values — validate presence, never log values.
        // Mismo criterio de CreateVeriffSessionAsync (config incompleta =
        // problema persistente, no transitorio), pero aquí se reporta como
        // VeriffDecisionFetchResult en vez de lanzar — este cliente está
        // diseñado para que el futuro orquestador clasifique errores
        // exclusivamente vía el resultado, sin mezclar excepciones y flags.
        var apiKey  = _config["VERIFF_API_KEY"];
        var baseUrl = _config["VERIFF_BASE_URL"];
        var secret  = _config["VERIFF_SHARED_SECRET"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("Veriff GET /decision: configuración incompleta.");
            return new VeriffDecisionFetchResult(false, false, false, null, null, "Veriff no configurado.");
        }

        // X-HMAC-SIGNATURE saliente: HMAC-SHA256 sobre el sessionId exacto
        // (no sobre un body — este GET no tiene body), hex minúsculas. No
        // reutiliza ValidateVeriffSignature (esa valida firmas ENTRANTES del
        // webhook, semántica distinta) ni el cálculo inline de
        // CreateVeriffSessionAsync (fuera de alcance tocarlo en este
        // sub-paso) — usa el helper mínimo ComputeVeriffHmacHex, reutilizable
        // por ambos en el futuro sin duplicar la fórmula.
        var hmacHex = ComputeVeriffHmacHex(secret, sessionId);

        var client = _http.CreateClient();
        var req    = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl.TrimEnd('/')}/v1/sessions/{sessionId}/decision");
        req.Headers.TryAddWithoutValidation("X-AUTH-CLIENT",    apiKey);
        req.Headers.TryAddWithoutValidation("X-HMAC-SIGNATURE", hmacHex);
        req.Headers.TryAddWithoutValidation("Content-Type",     "application/json");

        _logger.LogInformation("Veriff GET /decision: llamada iniciada.");

        HttpResponseMessage httpResp;
        try
        {
            httpResp = await client.SendAsync(req, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelación explícita del caller — se propaga tal cual, nunca
            // se reclasifica como "timeout de Veriff".
            throw;
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException interno del HttpClient (su propio
            // timeout, no configurado explícitamente en este sub-paso —
            // comportamiento por defecto), no causado por el ct del caller.
            _logger.LogWarning("Veriff GET /decision: timeout de transporte.");
            return new VeriffDecisionFetchResult(false, false, true, null, null, "Timeout consultando Veriff.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Veriff GET /decision: fallo de conexión. ExceptionType={Type}", ex.GetType().Name);
            return new VeriffDecisionFetchResult(false, false, true, null, null, "Fallo de conexión con Veriff.");
        }

        // Commit 5.6.1 — using (httpResp) envuelve exclusivamente el manejo
        // del lifetime del HttpResponseMessage ya recibido; ninguna rama,
        // clasificación HTTP, lectura de body ni log de abajo cambia — solo
        // se agrega indentación y el Dispose garantizado al salir por
        // cualquier return.
        using (httpResp)
        {
            var status = (int)httpResp.StatusCode;
            _logger.LogInformation("Veriff GET /decision: HTTP {Status} recibido.", status);

            if (status == 200)
            {
                string body;
                try
                {
                    body = await httpResp.Content.ReadAsStringAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    _logger.LogWarning("Veriff GET /decision: fallo leyendo el cuerpo de la respuesta.");
                    return new VeriffDecisionFetchResult(false, false, true, status, null, "Fallo leyendo la respuesta de Veriff.");
                }

                JsonElement root;
                try { root = JsonSerializer.Deserialize<JsonElement>(body); }
                catch
                {
                    _logger.LogWarning("Veriff GET /decision: JSON de respuesta ilegible.");
                    return new VeriffDecisionFetchResult(false, false, false, status, null, "Respuesta de Veriff no es JSON válido.");
                }

                // root.status aquí SOLO valida que la llamada fue exitosa — jamás
                // se usa como decisión ni como fallback de decisión (ver
                // comentario del método).
                var rootStatus = root.TryGetProperty("status", out var sv) ? sv.GetString() : null;
                if (rootStatus != "success")
                {
                    _logger.LogWarning("Veriff GET /decision: root.status inesperado.");
                    return new VeriffDecisionFetchResult(false, false, false, status, null, "root.status distinto de 'success'.");
                }

                var hasVerification = root.TryGetProperty("verification", out var verificationEl)
                                       && verificationEl.ValueKind != JsonValueKind.Null;

                if (!hasVerification)
                {
                    // verification ausente o null = decisión todavía no
                    // disponible — resultado exitoso, sin decisión que procesar.
                    _logger.LogInformation("Veriff GET /decision: verification no disponible todavía.");
                    return new VeriffDecisionFetchResult(true, true, false, status, null, null);
                }

                if (verificationEl.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogWarning("Veriff GET /decision: verification presente pero no es un objeto.");
                    return new VeriffDecisionFetchResult(false, false, false, status, null, "verification con forma inesperada.");
                }

                var parsed = ParseVeriffDecision(verificationEl);
                if (parsed == null)
                {
                    // ParseVeriffDecision solo devuelve null si verification no
                    // es un objeto válido — ya validado arriba, así que esta
                    // rama es puramente defensiva (contrato inesperado), nunca
                    // "sin decisión".
                    _logger.LogWarning("Veriff GET /decision: verification no pudo interpretarse.");
                    return new VeriffDecisionFetchResult(false, false, false, status, null, "verification no pudo interpretarse.");
                }

                // parsed.Decision puede ser null si Veriff no incluyó
                // verification.status — no se inventa ninguna decisión; el
                // futuro orquestador decide qué hacer con SinDecision=false pero
                // Decision.Decision=null.
                _logger.LogInformation("Veriff GET /decision: verification disponible.");
                return new VeriffDecisionFetchResult(true, false, false, status, parsed, null);
            }

            // Clasificación por código HTTP para todo lo que no fue 200.
            if (status is 400 or 401 or 404)
            {
                _logger.LogWarning("Veriff GET /decision: error definitivo. HttpStatus={Status}", status);
                return new VeriffDecisionFetchResult(false, false, false, status, null, $"Veriff respondió {status}.");
            }

            if (status >= 500)
            {
                // 500 documentado explícitamente como transitorio; cualquier
                // otro 5xx no documentado se trata igual, conservadoramente.
                _logger.LogWarning("Veriff GET /decision: error transitorio. HttpStatus={Status}", status);
                return new VeriffDecisionFetchResult(false, false, true, status, null, $"Veriff respondió {status}.");
            }

            // Código no documentado y no 5xx — clasificación conservadora como
            // error definitivo, sin inventar semántica de negocio.
            _logger.LogWarning("Veriff GET /decision: código HTTP no documentado. HttpStatus={Status}", status);
            return new VeriffDecisionFetchResult(false, false, false, status, null, $"Veriff respondió código no documentado {status}.");
        }
    }

    // Helper mínimo de firma HMAC saliente hacia Veriff — HMAC-SHA256 sobre
    // los bytes UTF-8 exactos de `data`, hex minúsculas. Reutilizable por
    // cualquier llamada saliente futura que necesite este mismo formato
    // (Veriff lo usa igual en ambos sentidos); no reutiliza
    // ValidateVeriffSignature (valida firmas entrantes, no genera salientes)
    // ni el cálculo inline de CreateVeriffSessionAsync (no tocado en este
    // sub-paso).
    private static string ComputeVeriffHmacHex(string secret, string data)
    {
        var keyBytes  = Encoding.UTF8.GetBytes(secret.Trim());
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(dataBytes)).ToLowerInvariant();
    }

    // ── Veriff webhook decision processing — adaptador delgado (Commit 5) ──────
    // Called only after signature is validated.
    // Does NOT log raw body, PII, documents, or biometrics.
    // Conserva únicamente lo específico del webhook: parseo del rawBody,
    // root.status como fallback exclusivo de este adaptador (nunca
    // reutilizado por el futuro cliente GET /decision, donde root.status
    // significa otra cosa), y la resolución de idKycVerificacion por
    // sessionId. Toda la lógica de negocio (máquina A-E, gate de identidad,
    // Casos 1-6, Auditoria, transacciones, locks) vive exclusivamente en
    // ProcesarDecisionVeriffAsync.
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

        root.TryGetProperty("verification", out var verification);
        var parsed = ParseVeriffDecision(verification);

        var sessionId  = parsed?.SessionId;
        var vendorData = parsed?.VendorData;
        // Prefer verification.status for decision; fall back to top-level status
        // — fallback exclusivo de este adaptador, ver comentario de arriba.
        var decision   = parsed?.Decision ?? topStatus;

        _logger.LogInformation(
            "Veriff webhook received: topStatus={Status} sessionId={SessionId} vendorData={VendorData}",
            topStatus, sessionId, vendorData);

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogWarning("Veriff webhook: missing sessionId — cannot process.");
            return new VeriffWebhookResult { Processed = false };
        }

        // parsed no puede ser null aquí: sessionId proviene de parsed.SessionId,
        // así que sessionId no vacío implica parsed no nulo.
        //
        // Sub-paso 5.3.1 — resuelve localmente si la decisión es reconocida
        // ANTES de calcular idKycVerificacion o invocar el núcleo: un evento
        // que no representa una decisión KYC (decision ausente, o presente
        // pero no mapeable — ej. "started"/"submitted") no debe adquirir
        // KYC_USUARIO ni abrir transacción, igual que en Commit 4.
        // MapVeriffDecision es pura (sin acceso a BD); llamarla aquí no
        // duplica ninguna regla de negocio — ProcesarDecisionVeriffAsync
        // conserva su propia llamada como defensa para futuros adaptadores
        // (ej. el cliente GET /decision).
        var estadoXpay = MapVeriffDecision(decision);

        if (estadoXpay == null)
        {
            _logger.LogInformation(
                "Veriff webhook: sessionId '{SessionId}' decisión '{Decision}' no es un evento terminal — acknowledged, no state update.",
                sessionId, decision);
            return new VeriffWebhookResult { Processed = false, SessionIdHint = sessionId };
        }

        // Find record by sessionId (prefer es_actual=true; fall back to most recent for this sessionId)
        var idKycVerificacion = await _db.KycVerificaciones
            .Where(k => k.SessionId == sessionId && k.EsActual)
            .Select(k => (long?)k.IdKycVerificacion)
            .FirstOrDefaultAsync()
            ?? await _db.KycVerificaciones
                .Where(k => k.SessionId == sessionId)
                .OrderByDescending(k => k.IdKycVerificacion)
                .Select(k => (long?)k.IdKycVerificacion)
                .FirstOrDefaultAsync();

        if (idKycVerificacion == null)
        {
            _logger.LogWarning(
                "Veriff webhook: sessionId '{SessionId}' not found in kyc_verificaciones — cannot update.",
                sessionId);
            return new VeriffWebhookResult { Processed = false, SessionIdHint = sessionId };
        }

        var resultado = await ProcesarDecisionVeriffAsync(
            idKycVerificacion.Value, parsed!, decision!, "webhook", CancellationToken.None);

        return new VeriffWebhookResult
        {
            Processed     = resultado.Procesado,
            EstadoMapeado = resultado.EstadoMapeado,
            SessionIdHint = sessionId,
        };
    }

    // ── Commit 5 — resultado neutral del núcleo compartido. Nunca se expone
    // vía HTTP directamente: cada adaptador (webhook, y el futuro GET
    // /decision) lo traduce a su propia forma de respuesta. Procesado=true
    // incluye las salidas idempotentes (Casos A/B/D/E, estado final
    // idéntico) — igual que VeriffWebhookResult.Processed ya hacía en
    // Commit 4. SinCambioPorIdempotencia distingue esas salidas de una
    // escritura real. SinDecision cubre tanto una decisión no mapeable como,
    // en el futuro, verification=null de GET. Errores técnicos (fila
    // inexistente, contención de locks) son excepciones, no flags.
    private sealed record VeriffDecisionProcessResult(
        bool    Procesado,
        string? EstadoMapeado,
        bool    SinCambioPorIdempotencia,
        bool    SinDecision
    );

    // ── Commit 5 — núcleo compartido de procesamiento de una decisión Veriff
    // ya parseada. Reutilizable por ProcessVeriffWebhookAsync y, en un paso
    // posterior, por el cliente GET /decision. No sabe si el origen fue
    // webhook o reconciliación salvo para etiquetar logs (parámetro
    // "origen") — nunca para alterar una regla de negocio.
    //
    // Serializa el procesamiento por usuario mediante
    // XPAY:KYC_USUARIO:{idUsuario}, adquirido ANTES de la máquina de orden,
    // con recarga fresca de KycVerificacion/Usuario bajo el lock — nunca
    // reutiliza un snapshot leído antes. Si la fila ya no es EsActual,
    // aplica el escritor histórico restringido: solo actualiza esa fila,
    // nunca Usuario/Persona/Auditoria/EsActual — CreateVeriffSessionAsync es
    // el único dueño de decidir qué sesión está vigente. Este método nunca
    // asigna kyc.EsActual.
    private async Task<VeriffDecisionProcessResult> ProcesarDecisionVeriffAsync(
        long idKycVerificacion,
        VeriffDecisionParsed parsed,
        string decision,
        string origen,
        CancellationToken ct)
    {
        // Micro-lectura fuera de cualquier transacción — únicamente para
        // construir la clave del lock. NO es el estado válido para la
        // máquina A-E; la fila real se recarga DESPUÉS de adquirir el lock.
        var idUsuario = await _db.KycVerificaciones.AsNoTracking()
            .Where(k => k.IdKycVerificacion == idKycVerificacion)
            .Select(k => (long?)k.IdUsuario)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"KycVerificacion {idKycVerificacion} no encontrada.");

        var claveLockUsuario = $"XPAY:KYC_USUARIO:{idUsuario}";

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var resultadoLockUsuario = await AppLockHelper.AdquirirAsync(_db, claveLockUsuario, ct);
            ValidarResultadoLockKycUsuario(resultadoLockUsuario);

            // Recarga fresca DESPUÉS del lock — nunca antes.
            var kyc = await _db.KycVerificaciones
                .Where(k => k.IdKycVerificacion == idKycVerificacion)
                .FirstOrDefaultAsync(ct);

            if (kyc == null)
                throw new InvalidOperationException($"KycVerificacion {idKycVerificacion} no encontrada tras adquirir el lock.");

            var usuario = await _db.Usuarios
                .Where(u => u.IdUsuario == kyc.IdUsuario)
                .FirstOrDefaultAsync(ct);

            if (usuario == null)
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning(
                    "Veriff decisión ({Origen}): usuario {IdUsuario} no encontrado para kyc {IdKyc}.",
                    origen, kyc.IdUsuario, idKycVerificacion);
                return new VeriffDecisionProcessResult(false, null, false, false);
            }

            var estadoXpay = MapVeriffDecision(decision);

            if (estadoXpay == null)
            {
                await tx.RollbackAsync(ct);
                _logger.LogInformation(
                    "Veriff decisión ({Origen}): decisión '{Decision}' no es un evento terminal — sin cambio de estado.",
                    origen, decision);
                return new VeriffDecisionProcessResult(false, null, false, true);
            }

            // ── Commit 4 — máquina de orden/idempotencia por attemptId/decisionTime ──
            // Basada exclusivamente en lo persistido en la fila recién
            // cargada (bajo el lock de sesión) — nunca en un snapshot previo.
            var attemptIdPersistido      = kyc.AttemptIdVeriff;
            var attemptIdEntrante        = parsed.AttemptId;
            var decisionTimePersistido   = kyc.DecisionTimeVeriff;
            var decisionTimeEntrante     = parsed.DecisionTimeUtc;
            var debeActualizarSeguimientoIntento = false;

            if (attemptIdPersistido != null)
            {
                // Fila ya rastreada por 034 — aplicar la máquina de orden.
                if (attemptIdEntrante != null && attemptIdEntrante == attemptIdPersistido)
                {
                    if (decision == kyc.Decision)
                    {
                        // CASO A: reentrega idéntica — mismo attempt, misma decisión.
                        await tx.RollbackAsync(ct);
                        _logger.LogInformation(
                            "Veriff decisión ({Origen}): mismo attemptId y misma decisión ya registrada — idempotente.",
                            origen);
                        return new VeriffDecisionProcessResult(true, kyc.EstadoKyc, true, false);
                    }
                    else
                    {
                        // CASO B: mismo attempt, decisión distinta — anomalía, no se sobrescribe.
                        await tx.RollbackAsync(ct);
                        _logger.LogWarning(
                            "Veriff decisión ({Origen}): mismo attemptId con decisión distinta a la ya registrada — anomalía, no se sobrescribe.",
                            origen);
                        return new VeriffDecisionProcessResult(true, kyc.EstadoKyc, true, false);
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
                            await tx.RollbackAsync(ct);
                            _logger.LogWarning(
                                "Veriff decisión ({Origen}): attemptId distinto pero decisionTime no es posterior al ya registrado — no se sobrescribe.",
                                origen);
                            return new VeriffDecisionProcessResult(true, kyc.EstadoKyc, true, false);
                        }
                    }
                    else
                    {
                        // CASO E: falta decisionTime en alguno de los dos lados — orden no determinable.
                        await tx.RollbackAsync(ct);
                        _logger.LogWarning(
                            "Veriff decisión ({Origen}): attemptId distinto pero no se puede determinar el orden (falta decisionTime) — no se sobrescribe.",
                            origen);
                        return new VeriffDecisionProcessResult(true, kyc.EstadoKyc, true, false);
                    }
                }
                else
                {
                    // attemptId entrante ausente mientras ya existe uno rastreado — no se puede
                    // comparar con certeza (mismo tratamiento conservador que el CASO E).
                    await tx.RollbackAsync(ct);
                    _logger.LogWarning(
                        "Veriff decisión ({Origen}): sin attemptId entrante pero ya existe uno rastreado — orden no determinable, no se sobrescribe.",
                        origen);
                    return new VeriffDecisionProcessResult(true, kyc.EstadoKyc, true, false);
                }
            }
            else if (attemptIdEntrante != null || decisionTimeEntrante.HasValue)
            {
                // CASO E-TRANSICIÓN: fila histórica (anterior a 034), sin attemptId rastreado
                // todavía — se trata como el primer intento rastreado, sin bloquear.
                debeActualizarSeguimientoIntento = true;
            }

            // Idempotency: if already in this exact final state, skip without error
            var estadosFinales = new HashSet<string>(StringComparer.Ordinal)
                { "APROBADO", "RECHAZADO", "EXPIRADO", "ERROR" };

            if (estadosFinales.Contains(kyc.EstadoKyc) && kyc.EstadoKyc == estadoXpay && !debeActualizarSeguimientoIntento)
            {
                await tx.RollbackAsync(ct);
                _logger.LogInformation(
                    "Veriff decisión ({Origen}): ya en estado final '{Estado}' — idempotent skip.",
                    origen, estadoXpay);
                return new VeriffDecisionProcessResult(true, estadoXpay, true, false);
            }

            var reason     = parsed.Reason;
            var vendorData = parsed.VendorData;

            // ── Commit 5 — bifurcación EsActual: sesión histórica vs vigente ──
            if (!kyc.EsActual)
            {
                // Sesión histórica: se registra la decisión únicamente en esta
                // fila. Nunca se toca EsActual, Usuario ni Persona, y nunca se
                // genera Auditoria KYC_IDENTIDAD_* — CreateVeriffSessionAsync
                // es el único dueño de decidir qué sesión está vigente.
                kyc.EstadoKyc          = estadoXpay;
                kyc.Decision           = decision;
                kyc.Reason             = reason;
                kyc.FechaDecision      = DateTime.UtcNow;
                kyc.FechaActualizacion = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(vendorData)) kyc.VendorData = vendorData;
                if (attemptIdEntrante != null) kyc.AttemptIdVeriff = attemptIdEntrante;
                if (decisionTimeEntrante.HasValue) kyc.DecisionTimeVeriff = decisionTimeEntrante;

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Veriff decisión ({Origen}): sesión histórica (EsActual=false) actualizada — sin propagar a Usuario/Persona.",
                    origen);
                return new VeriffDecisionProcessResult(true, estadoXpay, false, false);
            }

            // ── Commit 4 — consolidación de identidad Veriff → Persona (Casos 1-6) ──
            // Solo aplica cuando la decisión mapeada es APROBADO, la sesión es
            // vigente (EsActual=true) y el gate de 4 campos está completo
            // (Casos 1, 3, 4, 5, 6 — requieren lock exclusivo por documento).
            // Si está incompleto, es el Caso 2 (sin lock, sin IdentidadVerificada).
            if (estadoXpay == "APROBADO")
            {
                var gateCompleto =
                    !string.IsNullOrWhiteSpace(parsed.FirstName) &&
                    !string.IsNullOrWhiteSpace(parsed.LastName) &&
                    !string.IsNullOrWhiteSpace(parsed.DocumentType) &&
                    !string.IsNullOrWhiteSpace(parsed.DocumentNumber);

                if (!gateCompleto)
                {
                    // CASO 2 — aprobado pero incompleto para el gate. Antes de
                    // escribir cualquier campo crudo se verifica si la Persona ya
                    // tenía la identidad consolidada: un payload incompleto NUNCA
                    // debe sobrescribir parcialmente una identidad ya verificada.
                    var personaIncompleta = await _db.Personas
                        .Where(p => p.IdPersona == usuario.IdPersona)
                        .FirstOrDefaultAsync(ct);

                    if (personaIncompleta == null)
                    {
                        _logger.LogWarning(
                            "Veriff decisión ({Origen}): idUsuario={IdUsuario} sin Persona asociada — no se puede persistir identidad incompleta.",
                            origen, usuario.IdUsuario);
                    }
                    else if (personaIncompleta.IdentidadVerificada)
                    {
                        // CASO 2b — Persona ya verificada + payload incompleto — no
                        // se toca ningún campo de identidad.
                        _logger.LogInformation(
                            "Veriff decisión ({Origen}): idUsuario={IdUsuario} caso identidad={Caso} — payload incompleto sobre Persona ya verificada, sin cambios.",
                            origen, usuario.IdUsuario, IdentidadCaso.Incompleta);
                    }
                    else
                    {
                        var huboCambio = false;
                        if (!string.IsNullOrWhiteSpace(parsed.FirstName))      { personaIncompleta.NombreVerificadoCompleto   = parsed.FirstName;      huboCambio = true; }
                        if (!string.IsNullOrWhiteSpace(parsed.LastName))       { personaIncompleta.ApellidoVerificadoCompleto = parsed.LastName;       huboCambio = true; }
                        if (!string.IsNullOrWhiteSpace(parsed.DocumentType))   { personaIncompleta.TipoDocumentoVeriffRaw     = parsed.DocumentType;   huboCambio = true; }
                        if (!string.IsNullOrWhiteSpace(parsed.DocumentNumber)) { personaIncompleta.NumeroDocumentoVerificado  = parsed.DocumentNumber;  huboCambio = true; }
                        if (parsed.DateOfBirth.HasValue)                       { personaIncompleta.FechaNacimiento            = parsed.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue); huboCambio = true; }

                        if (huboCambio) personaIncompleta.FechaActualizacion = DateTime.UtcNow;

                        // CASO 2a — auditoría: por definición del gate incompleto
                        // siempre falta al menos uno de los 4 campos obligatorios.
                        var camposFaltantes = new List<string>();
                        if (string.IsNullOrWhiteSpace(parsed.FirstName))      camposFaltantes.Add("FIRST_NAME");
                        if (string.IsNullOrWhiteSpace(parsed.LastName))       camposFaltantes.Add("LAST_NAME");
                        if (string.IsNullOrWhiteSpace(parsed.DocumentType))   camposFaltantes.Add("DOCUMENT_TYPE");
                        if (string.IsNullOrWhiteSpace(parsed.DocumentNumber)) camposFaltantes.Add("DOCUMENT_NUMBER");

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
                            "Veriff decisión ({Origen}): idUsuario={IdUsuario} caso identidad={Caso} — gate incompleto, sin lock, sin IdentidadVerificada.",
                            origen, usuario.IdUsuario, IdentidadCaso.Incompleta);
                    }
                }
                else
                {
                    // Gate completo — requiere lock exclusivo por documento antes
                    // de evaluar y, si aplica, escribir Persona (Casos 1, 3, 4, 5,
                    // 6). Se adquiere SIEMPRE después de KYC_USUARIO, dentro de la
                    // misma transacción ya abierta.
                    var idUnidadNegocioPersona = await _db.Personas.AsNoTracking()
                        .Where(p => p.IdPersona == usuario.IdPersona)
                        .Select(p => p.IdUnidadNegocio)
                        .FirstOrDefaultAsync(ct);

                    var documentoNormalizado = parsed.DocumentNumber!.Trim().ToUpperInvariant();
                    var claveLockDocumento = $"XPAY:IDENTIDAD_DOCUMENTO:{idUnidadNegocioPersona}:{documentoNormalizado}";

                    var resultadoLockDocumento = await AppLockHelper.AdquirirAsync(_db, claveLockDocumento, ct);
                    ValidarResultadoLockIdentidad(resultadoLockDocumento);

                    // Recarga rastreada de Persona DESPUÉS de adquirir el lock —
                    // ninguna lectura previa de su estado de identidad es de fiar.
                    var persona = await _db.Personas
                        .Where(p => p.IdPersona == usuario.IdPersona)
                        .FirstOrDefaultAsync(ct);

                    if (persona == null)
                    {
                        _logger.LogWarning(
                            "Veriff decisión ({Origen}): idUsuario={IdUsuario} sin Persona asociada — no se puede consolidar identidad.",
                            origen, usuario.IdUsuario);
                    }
                    else if (persona.IdentidadVerificada)
                    {
                        // Casos 4, 5, 6 — esta Persona ya estaba verificada. Nunca se
                        // sobrescribe: solo se clasifica para observabilidad.
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
                                "Veriff decisión ({Origen}): idUsuario={IdUsuario} caso identidad={Caso} — Persona ya verificada, sin cambios.",
                                origen, usuario.IdUsuario, IdentidadCaso.RevalidacionEquivalente);
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
                                "Veriff decisión ({Origen}): idUsuario={IdUsuario} caso identidad={Caso} — Persona ya verificada, sin cambios.",
                                origen, usuario.IdUsuario, IdentidadCaso.CambioDatos);
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
                                "Veriff decisión ({Origen}): idUsuario={IdUsuario} caso identidad={Caso} — Persona ya verificada, sin cambios.",
                                origen, usuario.IdUsuario, IdentidadCaso.CambioDocumento);
                        }
                    }
                    else
                    {
                        // Persona aún no verificada — verificar conflicto de documento
                        // con otra Persona antes de escribir (bajo el lock exclusivo).
                        var conflicto = await _db.Personas.AnyAsync(p =>
                            p.IdPersona != persona.IdPersona &&
                            p.IdUnidadNegocio == persona.IdUnidadNegocio &&
                            p.NumeroDocumentoVerificado != null &&
                            p.NumeroDocumentoVerificado.Trim().ToUpper() == documentoNormalizado &&
                            p.IdentidadVerificada, ct);

                        if (conflicto)
                        {
                            // CASO 3 — documento ya verificado en otra Persona: no se
                            // marca identidad, Persona no se toca.
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
                                "Veriff decisión ({Origen}): idUsuario={IdUsuario} caso identidad={Caso} — número de documento verificado ya pertenece a otra Persona.",
                                origen, usuario.IdUsuario, IdentidadCaso.DocumentoDuplicado);
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
                                "Veriff decisión ({Origen}): idUsuario={IdUsuario} caso identidad={Caso} — identidad consolidada.",
                                origen, usuario.IdUsuario, IdentidadCaso.Consolidada);
                        }
                    }
                }
            }

            // Escritura incondicional de kyc/usuario — EsActual NO se toca:
            // ya es true en esta rama (sesión vigente); ProcesarDecisionVeriffAsync
            // nunca decide vigencia, eso es responsabilidad exclusiva de
            // CreateVeriffSessionAsync.
            kyc.EstadoKyc          = estadoXpay;
            kyc.Decision           = decision;
            kyc.Reason             = reason;
            kyc.FechaDecision      = DateTime.UtcNow;
            kyc.FechaActualizacion = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(vendorData)) kyc.VendorData = vendorData;
            if (attemptIdEntrante != null) kyc.AttemptIdVeriff = attemptIdEntrante;
            if (decisionTimeEntrante.HasValue) kyc.DecisionTimeVeriff = decisionTimeEntrante;

            usuario.EstadoKycActual       = estadoXpay;
            usuario.FechaKycActualizacion = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (EsViolacionDocumentoVerificadoDuplicado(ex))
            {
                // CASO 3 tardío — el índice único detectó, dentro de esta misma
                // transacción, un conflicto que la consulta previa (bajo el mismo
                // lock) no vio. No se reintenta con una segunda escritura: se
                // descarta TODO este intento y se deja que una futura entrega o
                // reconciliación lo procese bajo el estado ya consistente de la base.
                await tx.RollbackAsync(ct);
                _logger.LogWarning(
                    "Veriff decisión ({Origen}): idUsuario={IdUsuario} caso identidad={Caso} detectado tardíamente por el índice único — se descarta este intento completo.",
                    origen, usuario.IdUsuario, IdentidadCaso.DocumentoDuplicado);
                return new VeriffDecisionProcessResult(false, null, false, false);
            }

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Veriff decisión ({Origen}) procesada: idUsuario={IdUsuario} estado={Estado}",
                origen, kyc.IdUsuario, estadoXpay);

            return new VeriffDecisionProcessResult(true, estadoXpay, false, false);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Commit 5 — orquestador de reconciliación administrativa. Internal
    // (no público): único consumidor es KycController, en el mismo
    // ensamblado — su resultado (ReconciliacionVeriffResultado) tampoco es
    // público, así que el método no puede serlo (CS0050). No abre
    // transacción ni adquiere ningún AppLock por sí mismo — delega TODA la
    // lógica transaccional/de negocio a ProcesarDecisionVeriffAsync
    // (reutilizado tal cual) y toda la llamada HTTP a FetchVeriffDecisionAsync
    // (reutilizado tal cual). El GET a Veriff se completa por completo antes
    // de que exista cualquier transacción SQL o lock — nunca se sostiene
    // KYC_USUARIO durante el round-trip de red.
    //
    // La validación de elegibilidad de abajo es exclusivamente best-effort:
    // solo evita una llamada HTTP inútil cuando la fila obviamente no
    // califica. NO es una garantía transaccional — el estado puede cambiar
    // legítimamente durante el round-trip HTTP (otro webhook, una nueva
    // sesión creada por CreateVeriffSessionAsync, etc.). La corrección real
    // la da exclusivamente ProcesarDecisionVeriffAsync, que recarga el
    // estado fresco bajo XPAY:KYC_USUARIO:{idUsuario} y aplica la máquina
    // A-E + la regla de sesión histórica sobre esos datos, nunca sobre este
    // snapshot inicial.
    internal async Task<ReconciliacionVeriffResultado> ReconciliarVeriffAsync(long idKycVerificacion, CancellationToken ct)
    {
        // Lookup best-effort, de solo lectura — nunca se usa para decidir
        // una escritura, solo para decidir si vale la pena llamar a Veriff.
        var candidata = await _db.KycVerificaciones.AsNoTracking()
            .Where(k => k.IdKycVerificacion == idKycVerificacion)
            .Select(k => new { k.Proveedor, k.EsActual, k.SessionId, k.EstadoKyc })
            .FirstOrDefaultAsync(ct);

        if (candidata == null)
            return new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.FilaNoEncontrada, null, "KycVerificacion no encontrada.");

        var elegible =
            candidata.Proveedor == "VERIFF" &&
            candidata.EsActual &&
            !string.IsNullOrWhiteSpace(candidata.SessionId) &&
            candidata.EstadoKyc is "PENDIENTE" or "EN_REVISION";

        if (!elegible)
            return new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.NoElegible, candidata.EstadoKyc, "La sesión no es elegible para reconciliación (proveedor, vigencia, sessionId o estado actual no califican).");

        // GET a Veriff — completamente fuera de cualquier transacción/lock.
        var fetch = await FetchVeriffDecisionAsync(candidata.SessionId!, ct);

        if (!fetch.Exito)
        {
            if (fetch.HttpStatus == 404)
                return new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.SesionNoEncontradaEnVeriff, null, "Sesión no encontrada en Veriff.");

            return fetch.ErrorTransitorio
                ? new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.ErrorTransitorioVeriff, null, fetch.Error)
                : new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.ErrorDefinitivoVeriff, null, fetch.Error);
        }

        // fetch.Decision.Decision es la única fuente de decisión válida —
        // jamás root.status (FetchVeriffDecisionAsync ni siquiera lo expone).
        if (fetch.SinDecision || fetch.Decision == null || string.IsNullOrWhiteSpace(fetch.Decision.Decision))
            return new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.SinDecisionTodavia, null, null);

        var resultado = await ProcesarDecisionVeriffAsync(
            idKycVerificacion, fetch.Decision, fetch.Decision.Decision, "reconciliacion-admin", ct);

        if (resultado.SinDecision)
            return new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.SinDecisionTodavia, resultado.EstadoMapeado, null);

        if (resultado.SinCambioPorIdempotencia)
            return new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.ProcesadoSinCambiosPorIdempotencia, resultado.EstadoMapeado, null);

        if (resultado.Procesado)
            return new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.ProcesadoConCambios, resultado.EstadoMapeado, null);

        // Procesado=false sin SinDecision: usuario inexistente (flag) o Caso
        // 3 tardío (backstop del índice) — no son errores de Veriff, son
        // desenlaces internos de XPAY para este intento específico.
        return new ReconciliacionVeriffResultado(ReconciliacionVeriffCategoria.ErrorTecnicoInesperado, null, "No se pudo completar el procesamiento de este intento.");
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

    // ── Commit 5 — interpretación del resultado de AppLockHelper.AdquirirAsync
    // para la clave XPAY:KYC_USUARIO:{idUsuario} ──
    // Deliberadamente NO usa AppLockHelper.ValidarResultado (dominio
    // Caja/Cierre) ni ValidarResultadoLockIdentidad (dominio distinto: ese
    // protege unicidad de documento entre Personas, este serializa el ciclo
    // de vida de sesiones KYC de un mismo usuario). No toca BD, no toca
    // Persona, no crea Auditoria, no recibe ni loguea idUsuario,
    // idKycVerificacion, sessionId ni la clave del lock — solo interpreta el
    // código entero devuelto por sp_getapplock. Conectado a
    // CreateVeriffSessionAsync (Sub-paso 5.2) y a ProcesarDecisionVeriffAsync
    // (Sub-paso 5.3).
    private static void ValidarResultadoLockKycUsuario(int resultado)
    {
        switch (resultado)
        {
            case 0:
            case 1:
                return;
            case -1:
                throw new KycUsuarioConcurrenteException(
                    "Tiempo de espera agotado esperando otra sincronización KYC concurrente de este usuario. Intenta de nuevo.");
            case -2:
                throw new KycUsuarioConcurrenteException(
                    "La solicitud de sincronización KYC del usuario fue cancelada. Intenta de nuevo.");
            case -3:
                throw new KycUsuarioConcurrenteException(
                    "Se detectó un interbloqueo con otra sincronización KYC concurrente de este usuario. Intenta de nuevo.");
            default:
                // Mismo criterio que el "default" de ValidarResultadoLockIdentidad:
                // error técnico de la llamada en sí (parámetros/infraestructura), no
                // contención legítima — se relanza sin tipar, sin incluir la clave
                // del lock ni ningún dato del usuario.
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
