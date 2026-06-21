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
    /// Placeholder — Fase 62 conectará el SDK Veriff sandbox.
    /// Devuelve 501 hasta que las credenciales sandbox estén configuradas
    /// en Azure App Settings (nunca en el repositorio).
    /// </summary>
    [HttpPost("veriff/session")]
    [Authorize]
    public IActionResult VeriffSession()
    {
        return StatusCode(501, new
        {
            success = false,
            message = "Veriff session creation not implemented yet. " +
                      "Configure VERIFF_API_KEY in Azure App Settings to enable. " +
                      "Fase 62 pending.",
            fase    = "62-veriff-sandbox",
        });
    }

    /// <summary>
    /// POST /api/kyc/veriff/webhook
    /// Stub seguro — Fase 62 implementará validación HMAC de firma Veriff
    /// y actualización real de estado KYC.
    /// Devuelve 200 para evitar reintentos de Veriff en ambiente QA.
    /// SIN lógica de negocio ni actualización de datos en esta fase.
    /// </summary>
    [HttpPost("veriff/webhook")]
    public IActionResult VeriffWebhook()
    {
        // Stub: acknowledge receipt without processing.
        // Phase 62 will add HMAC signature validation and state update logic.
        return Ok(new { received = true });
    }
}
