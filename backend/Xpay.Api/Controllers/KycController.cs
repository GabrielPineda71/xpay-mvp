using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    /// Stub seguro — Fase 63 implementará validación HMAC-SHA256 con VERIFF_SHARED_SECRET
    /// y actualización real de estado KYC.
    /// Devuelve 200 para evitar reintentos de Veriff en ambiente QA.
    /// SIN lógica de negocio ni actualización de datos en esta fase.
    /// </summary>
    [HttpPost("veriff/webhook")]
    public IActionResult VeriffWebhook()
    {
        // Stub: acknowledge receipt without processing.
        // Phase 63 will add HMAC-SHA256 signature validation and state update logic.
        return Ok(new { received = true });
    }
}
