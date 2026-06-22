using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/kyc")]
public class KycController : ControllerBase
{
    private readonly KycService    _kyc;
    private readonly AuditLogService _audit;

    public KycController(KycService kyc, AuditLogService audit)
    {
        _kyc   = kyc;
        _audit = audit;
    }

    /// <summary>
    /// GET /api/kyc/mi-estado
    /// Devuelve el estado KYC del usuario autenticado.
    /// No expone datos sensibles ni secretos Veriff.
    /// </summary>
    [HttpGet("mi-estado")]
    [Authorize]
    public async Task<IActionResult> MiEstado()
    {
        if (!long.TryParse(User.FindFirst("idUsuario")?.Value, out var idUsuario) || idUsuario <= 0)
            return Unauthorized(new { success = false, message = "Token inválido." });

        try
        {
            var data = await _kyc.GetMiEstadoAsync(idUsuario);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch
        {
            return StatusCode(500, new { success = false, message = "Error interno consultando estado KYC." });
        }
    }

    /// <summary>
    /// POST /api/kyc/qa/simular-estado
    /// Solo QA/Demo. Permite a ADMIN_XPAY o SUPERUSUARIO simular un estado KYC
    /// para qa.usuario1 o qa.usuario2. No conecta a Veriff real.
    /// Body: { "usuario": "qa.usuario1", "estadoKyc": "APROBADO" }
    /// </summary>
    [HttpPost("qa/simular-estado")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> SimularEstadoQa([FromBody] SimularEstadoKycRequest request)
    {
        _audit.LogSensitiveAction(HttpContext, "KYC_QA_SIMULATE_ATTEMPT",
            new { usuario = request.Usuario, estadoKyc = request.EstadoKyc });
        try
        {
            var msg = await _kyc.SimularEstadoQaAsync(request);
            _audit.LogSensitiveAction(HttpContext, "KYC_QA_SIMULATE_SUCCESS",
                new { usuario = request.Usuario, estadoKyc = request.EstadoKyc });
            return Ok(new { success = true, message = msg });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch
        {
            return StatusCode(500, new { success = false, message = "Error interno simulando estado KYC." });
        }
    }

    /// <summary>
    /// POST /api/kyc/veriff/session
    /// Crea sesión real en Veriff sandbox.
    /// Lee VERIFF_API_KEY / VERIFF_SHARED_SECRET / VERIFF_BASE_URL desde Azure App Settings.
    /// No guarda ni retorna API keys. No envía datos personales.
    /// VendorData = XPAY-QA-USUARIO-{idUsuario} — tracking interno sin PII.
    /// Guarda en kyc_verificaciones y actualiza usuarios.estado_kyc_actual = PENDIENTE.
    /// </summary>
    [HttpPost("veriff/session")]
    [Authorize]
    public async Task<IActionResult> VeriffSession()
    {
        if (!long.TryParse(User.FindFirst("idUsuario")?.Value, out var idUsuario) || idUsuario <= 0)
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "KYC_VERIFF_SESSION_ATTEMPT", new { idUsuario });
        try
        {
            var data = await _kyc.CreateVeriffSessionAsync(idUsuario);
            _audit.LogSensitiveAction(HttpContext, "KYC_VERIFF_SESSION_CREATED",
                new { idUsuario, sessionId = data.SessionId });
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.StartsWith("Veriff sandbox no configurado") ? 503 : 400;
            return StatusCode(code, new { success = false, message = ex.Message });
        }
        catch
        {
            return StatusCode(500, new { success = false, message = "Error interno iniciando verificación." });
        }
    }

    /// <summary>
    /// POST /api/kyc/veriff/webhook
    /// Veriff decision webhook — no [Authorize], called by Veriff server directly.
    ///
    /// Security (Fase 63):
    ///   1. Reads raw body (EnableBuffering) to validate HMAC-SHA256.
    ///   2. Header: x-hmac-signature (hex-encoded HMAC-SHA256 of raw body, key = VERIFF_SHARED_SECRET).
    ///   3. Constant-time comparison (CryptographicOperations.FixedTimeEquals).
    ///   4. Missing or invalid signature → 401, no state change, audit logged.
    ///   5. Valid signature → ProcessVeriffWebhookAsync updates kyc_verificaciones + usuarios.
    ///
    /// Logs: event, sessionId, vendorData, mapped state, result.
    /// Never logs: VERIFF_SHARED_SECRET, raw body, person data, biometrics, documents.
    /// </summary>
    [HttpPost("veriff/webhook")]
    public async Task<IActionResult> VeriffWebhook()
    {
        // Read raw body before any model binding — required for HMAC computation
        Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var signature = Request.Headers["x-hmac-signature"].FirstOrDefault();

        if (!_kyc.ValidateVeriffSignature(rawBody, signature))
        {
            _audit.LogSensitiveAction(HttpContext, "KYC_WEBHOOK_SIGNATURE_INVALID",
                new { signaturePresent = !string.IsNullOrEmpty(signature) });
            return Unauthorized(new { received = false, error = "Signature invalid or missing." });
        }

        _audit.LogSensitiveAction(HttpContext, "KYC_WEBHOOK_SIGNATURE_VALID", new { });

        VeriffWebhookResult result;
        try
        {
            result = await _kyc.ProcessVeriffWebhookAsync(rawBody);
        }
        catch (Exception ex)
        {
            _audit.LogSensitiveAction(HttpContext, "KYC_WEBHOOK_PROCESSING_ERROR",
                new { error = ex.GetType().Name });
            return StatusCode(500, new { received = true, processed = false });
        }

        _audit.LogSensitiveAction(HttpContext, "KYC_WEBHOOK_PROCESSED",
            new { processed = result.Processed, estadoMapeado = result.EstadoMapeado });

        return Ok(new { received = true, processed = result.Processed });
    }
}
