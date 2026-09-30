using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// XPAY-594 — M5-T6/T7: endpoint receptor del webhook de pago ENTRANTE de
// Passport (pago hacia una llave XPAY o un QR).
//
// Patrón estructural tomado de KycController.VeriffWebhook, pero SIN acoplar
// KYC con Passport: componentes, configuración y secreto son propios.
//
// Este endpoint NO mueve dinero. No llama a Wallet, Ledger, breb_retiros,
// ApplyPassportPaymentStatusAsync ni a ninguna API de Passport.
[ApiController]
[Route("api/passport")]
public class PassportWebhookController : ControllerBase
{
    private readonly IPassportWebhookSignatureValidator _signature;
    private readonly PassportWebhookService             _service;
    private readonly AuditLogService                    _audit;
    private readonly ILogger<PassportWebhookController> _logger;

    public PassportWebhookController(
        IPassportWebhookSignatureValidator signature,
        PassportWebhookService service,
        AuditLogService audit,
        ILogger<PassportWebhookController> logger)
    {
        _signature = signature;
        _service   = service;
        _audit     = audit;
        _logger    = logger;
    }

    /// <summary>
    /// POST /api/passport/webhook
    ///
    /// Contrato de respuesta cerrado en XPAY-593 (confirmado por Passport):
    /// Passport reintenta ante cualquier respuesta distinta de 200.
    ///
    ///   200 — entrega válida procesada, o duplicado ya procesado, o evento no
    ///         entrante/desconocido registrado (reintentar no cambiaría nada).
    ///   400 — cuerpo no interpretable como JSON: no es posible identificar el
    ///         evento ni construir la forma canónica.
    ///   401 — firma ausente o inválida cuando el modo es ENFORCE.
    ///   500 — fallo interno real; el reintento de Passport es el comportamiento
    ///         deseado.
    ///
    /// Nunca registra ni persiste: secreto, firma, material HMAC, cuerpo crudo
    /// ni los bloques sender/receiver (PII y datos bancarios).
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        // El cuerpo crudo se necesita ANTES de cualquier model binding para
        // poder re-serializarlo en forma canónica y verificar la firma.
        Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var signatureHeader = Request.Headers[PassportWebhookOptions.HeaderSignature].FirstOrDefault();
        var timestampHeader = Request.Headers[PassportWebhookOptions.HeaderTimestamp].FirstOrDefault();

        _logger.LogInformation(
            "Passport webhook incoming: bodyLen={Len} hasSignature={HasSig} hasTimestamp={HasTs}",
            rawBody.Length,
            !string.IsNullOrWhiteSpace(signatureHeader),
            !string.IsNullOrWhiteSpace(timestampHeader));

        var signature = _signature.Validate(rawBody, signatureHeader, timestampHeader);

        if (signature.ShouldReject)
        {
            // Una entrega no autenticada NO se persiste: evita que un tercero
            // pueda llenar la tabla de eventos.
            _audit.LogSensitiveAction(HttpContext, "PASSPORT_WEBHOOK_SIGNATURE_REJECTED",
                new { outcome = signature.Outcome.ToString() });

            return Unauthorized(new { received = true, processed = false });
        }

        if (!PassportWebhookPayloadParser.TryParse(rawBody, out var payload))
        {
            _audit.LogSensitiveAction(HttpContext, "PASSPORT_WEBHOOK_PAYLOAD_UNPARSEABLE",
                new { bodyLen = rawBody.Length });

            return BadRequest(new { received = true, processed = false });
        }

        _audit.LogSensitiveAction(HttpContext, "PASSPORT_WEBHOOK_RECEIVED",
            new
            {
                evento         = payload.EventName,
                kind           = payload.Kind.ToString(),
                signatureValid = signature.SignatureValid,
                outcome        = signature.Outcome.ToString(),
            });

        PassportWebhookProcessOutcome outcome;
        try
        {
            outcome = await _service.ProcessAsync(
                rawBody, payload, signature.SignatureValid, cancellationToken);
        }
        catch (Exception ex)
        {
            _audit.LogSensitiveAction(HttpContext, "PASSPORT_WEBHOOK_PROCESSING_ERROR",
                new { error = ex.GetType().Name });

            // 500 provoca el reintento documentado de Passport, que es
            // exactamente lo deseado ante un fallo transitorio.
            return StatusCode(500, new { received = true, processed = false });
        }

        return Ok(new
        {
            received  = true,
            processed = outcome.Result == PassportWebhookProcessResult.Processed,
            result    = outcome.Result.ToString(),
        });
    }
}
