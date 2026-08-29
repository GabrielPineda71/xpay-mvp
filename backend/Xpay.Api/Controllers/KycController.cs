using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/kyc")]
public class KycController : ControllerBase
{
    private readonly KycService             _kyc;
    private readonly AuditLogService        _audit;
    private readonly ILogger<KycController> _logger;

    public KycController(KycService kyc, AuditLogService audit, ILogger<KycController> logger)
    {
        _kyc    = kyc;
        _audit  = audit;
        _logger = logger;
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
        catch (KycUsuarioConcurrenteException ex)
        {
            // Contención transitoria de sincronización KYC del usuario (p. ej.
            // doble-clic/doble-pestaña) — mismo criterio que
            // ReconciliarVeriff: no es un error de negocio/solicitud, nunca
            // se mapea a 400 ni a un 500 genérico.
            return StatusCode(503, new { success = false, message = ex.Message });
        }
        catch
        {
            return StatusCode(500, new { success = false, message = "Error interno iniciando verificación." });
        }
    }

    /// <summary>
    /// POST /api/kyc/admin/reconciliar-veriff
    /// Solo ADMIN_XPAY o SUPERUSUARIO. Reconcilia manualmente una fila
    /// kyc_verificaciones existente consultando GET /v1/sessions/{sessionId}/decision
    /// de Veriff — para sesiones que quedaron sin decisión de webhook (perdida,
    /// tardía, o nunca entregada).
    ///
    /// El sessionId consultado a Veriff sale EXCLUSIVAMENTE de la fila
    /// persistida en XPAY (nunca del request) — el request solo identifica
    /// QUÉ fila propia reconciliar, para que el operador nunca pueda hacer
    /// que XPAY consulte un sessionId externo arbitrario que no corresponda
    /// a una fila real ya existente.
    ///
    /// La validación de elegibilidad (proveedor VERIFF, EsActual, sessionId
    /// presente, estado PENDIENTE/EN_REVISION) es solo best-effort para
    /// evitar una llamada HTTP inútil — la garantía real de consistencia la
    /// da KycService.ProcesarDecisionVeriffAsync, que recarga el estado
    /// fresco bajo el lock XPAY:KYC_USUARIO:{idUsuario} antes de decidir
    /// cualquier escritura.
    ///
    /// Body: { "idKycVerificacion": 12 }
    /// </summary>
    [HttpPost("admin/reconciliar-veriff")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> ReconciliarVeriff([FromBody] ReconciliarVeriffRequest request)
    {
        _audit.LogSensitiveAction(HttpContext, "KYC_RECONCILIACION_VERIFF_ATTEMPT",
            new { idKycVerificacion = request.IdKycVerificacion });

        try
        {
            var resultado = await _kyc.ReconciliarVeriffAsync(request.IdKycVerificacion, HttpContext.RequestAborted);

            _audit.LogSensitiveAction(HttpContext, "KYC_RECONCILIACION_VERIFF_RESULTADO",
                new { idKycVerificacion = request.IdKycVerificacion, categoria = resultado.Categoria.ToString() });

            return resultado.Categoria switch
            {
                ReconciliacionVeriffCategoria.ProcesadoConCambios
                    or ReconciliacionVeriffCategoria.ProcesadoSinCambiosPorIdempotencia
                    or ReconciliacionVeriffCategoria.SinDecisionTodavia
                    => Ok(new { success = true, categoria = resultado.Categoria.ToString(), estadoKyc = resultado.EstadoMapeado }),

                ReconciliacionVeriffCategoria.FilaNoEncontrada
                    or ReconciliacionVeriffCategoria.SesionNoEncontradaEnVeriff
                    => NotFound(new { success = false, message = resultado.Mensaje ?? "No encontrado." }),

                ReconciliacionVeriffCategoria.NoElegible
                    or ReconciliacionVeriffCategoria.ErrorDefinitivoVeriff
                    => BadRequest(new { success = false, message = resultado.Mensaje ?? "Solicitud no válida." }),

                ReconciliacionVeriffCategoria.ErrorTransitorioVeriff
                    => StatusCode(503, new { success = false, message = resultado.Mensaje ?? "Error transitorio consultando Veriff." }),

                _ => StatusCode(500, new { success = false, message = "Error interno reconciliando la sesión." }),
            };
        }
        catch (KycUsuarioConcurrenteException ex)
        {
            // Contención transitoria de sincronización KYC del usuario — no
            // es un error de negocio/solicitud, nunca se mapea a 400.
            return StatusCode(503, new { success = false, message = ex.Message });
        }
        catch (IdentidadDocumentoConcurrenteException ex)
        {
            // Contención transitoria del lock de identidad-documento — mismo
            // criterio que arriba.
            return StatusCode(503, new { success = false, message = ex.Message });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            // El cliente/admin canceló o cerró la conexión — no es una falla
            // interna ni una contención transitoria. Se re-lanza (mismo
            // criterio que CajaVencidaSchedulerService.EjecutarBarridoSeguroAsync
            // con el apagado normal del host): ASP.NET Core ya sabe que la
            // conexión fue abortada y no intentará escribir ninguna
            // respuesta útil — construir un StatusCode aquí sería una
            // clasificación artificial de "error interno" para algo que no
            // lo es, y contaminaría el monitoreo de esta acción.
            throw;
        }
        catch
        {
            // Incluye InvalidOperationException del núcleo (fila
            // desaparecida entre el lookup best-effort y la recarga bajo
            // lock — defensivo, no debería ocurrir en operación normal) y
            // cualquier otra falla técnica no clasificada.
            return StatusCode(500, new { success = false, message = "Error interno reconciliando la sesión." });
        }
    }

    // Request mínimo del endpoint admin — nested, no DTO público nuevo: el
    // sessionId nunca viaja en el request, solo el identificador propio de
    // XPAY.
    public sealed class ReconciliarVeriffRequest
    {
        public long IdKycVerificacion { get; set; }
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

        // Part D: log names (not values) of signature-related headers for diagnostics
        var sigHeaderNames = Request.Headers.Keys
            .Where(k => k.StartsWith("x-", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("hmac", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("sign", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k);
        _logger.LogInformation(
            "Veriff webhook incoming: bodyLen={Len} sigHeaders=[{Headers}]",
            rawBody.Length,
            string.Join(", ", sigHeaderNames));

        // Read both possible signature headers; Veriff sends x-hmac-signature and x-signature
        var hmacSig   = Request.Headers["x-hmac-signature"].FirstOrDefault();
        var altSig    = Request.Headers["x-signature"].FirstOrDefault();
        var anyPresent = !string.IsNullOrEmpty(hmacSig) || !string.IsNullOrEmpty(altSig);

        _logger.LogInformation(
            "Veriff webhook sig lengths: x-hmac-signature={HmacLen} x-signature={AltLen}",
            hmacSig?.Trim().Length ?? 0,
            altSig?.Trim().Length ?? 0);

        // Try x-hmac-signature first, then x-signature as fallback header name
        var validHmac = !string.IsNullOrEmpty(hmacSig) && _kyc.ValidateVeriffSignature(rawBody, hmacSig);
        var validAlt  = !validHmac && !string.IsNullOrEmpty(altSig) && _kyc.ValidateVeriffSignature(rawBody, altSig);
        var validationPassed = validHmac || validAlt;

        if (validationPassed)
            _logger.LogInformation("Veriff webhook sig validated via: {Header}", validHmac ? "x-hmac-signature" : "x-signature");

        if (!validationPassed)
        {
            _audit.LogSensitiveAction(HttpContext, "KYC_WEBHOOK_SIGNATURE_INVALID",
                new { hmacPresent = !string.IsNullOrEmpty(hmacSig), altPresent = !string.IsNullOrEmpty(altSig) });
            return Unauthorized(new { received = false, error = "Signature invalid or missing." });
        }

        _audit.LogSensitiveAction(HttpContext, "KYC_WEBHOOK_SIGNATURE_VALID",
            new { via = validHmac ? "x-hmac-signature" : "x-signature" });

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
