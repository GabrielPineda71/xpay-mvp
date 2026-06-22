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
                Callback   = "https://xpay-api-qa.azurewebsites.net/api/kyc/veriff/webhook",
                VendorData = vendorData,
                Timestamp  = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
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
