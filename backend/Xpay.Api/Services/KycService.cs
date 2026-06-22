using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
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
        // Validate config exists — read presence only, never log values
        var apiKey    = _config["VERIFF_API_KEY"];
        var baseUrl   = _config["VERIFF_BASE_URL"];
        var hasSecret = !string.IsNullOrWhiteSpace(_config["VERIFF_SHARED_SECRET"]);

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl) || !hasSecret)
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
                // RedirectUrl instructs Veriff to redirect user back after completing verification.
                // Handled in frontend as ?kyc=return. Veriff V1 may ignore this field.
                RedirectUrl = "https://xpay-admin-qa.azurewebsites.net/mi-wallet?kyc=return",
            }
        };

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json     = JsonSerializer.Serialize(payload, jsonOpts);

        var client = _http.CreateClient();
        var req    = new HttpRequestMessage(
                         HttpMethod.Post,
                         $"{baseUrl.TrimEnd('/')}/v1/sessions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("X-AUTH-CLIENT", apiKey);

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
            _logger.LogWarning(
                "Veriff returned HTTP {Status} for user {IdUsuario}.",
                (int)httpResp.StatusCode, idUsuario);
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
        var topStatus  = root.TryGetProperty("status",       out var sv) ? sv.GetString() : null;
        var hasVerif   = root.TryGetProperty("verification", out var vv);
        var sessionId  = hasVerif && vv.TryGetProperty("id",         out var sid)  ? sid.GetString()  : null;
        var vendorData = hasVerif && vv.TryGetProperty("vendorData", out var vd)   ? vd.GetString()   : null;
        // Prefer verification.status for decision; fall back to top-level status
        var decision   = (hasVerif && vv.TryGetProperty("status",    out var vs)   ? vs.GetString()   : null)
                         ?? topStatus;
        var reason     = hasVerif && vv.TryGetProperty("reason",     out var vr)   ? vr.GetString()   : null;

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

        // Idempotency: if already in this exact final state, skip without error
        var estadosFinales = new HashSet<string>(StringComparer.Ordinal)
            { "APROBADO", "RECHAZADO", "EXPIRADO", "ERROR" };

        if (estadosFinales.Contains(kyc.EstadoKyc) && kyc.EstadoKyc == estadoXpay)
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

        // Transactional update of kyc record + user summary
        kyc.EstadoKyc          = estadoXpay;
        kyc.Decision           = decision;
        kyc.Reason             = reason;
        kyc.FechaDecision      = DateTime.UtcNow;
        kyc.FechaActualizacion = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(vendorData)) kyc.VendorData = vendorData;
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
