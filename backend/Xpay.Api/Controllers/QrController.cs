using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/qr")]
public class QrController : ControllerBase
{
    private readonly PagoQrService   _pagoQrService;
    private readonly WalletService   _walletService;
    private readonly AuditLogService _audit;
    private readonly ILogger<QrController> _logger;

    public QrController(PagoQrService pagoQrService, WalletService walletService, AuditLogService audit, ILogger<QrController> logger)
    {
        _pagoQrService = pagoQrService;
        _walletService = walletService;
        _audit         = audit;
        _logger        = logger;
    }

    private bool TryGetIdPersona(out long idPersona) =>
        long.TryParse(User.FindFirst("idPersona")?.Value, out idPersona) && idPersona > 0;

    private bool TryGetUsuarioId(out long idUsuario) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out idUsuario) && idUsuario > 0;

    // Fase 71.2-E-G: mismo helper que WalletsController.TryGetIdempotencyKey
    // — duplicado deliberadamente (mismo criterio ya usado por
    // TryGetIdPersona/TryGetUsuarioId en ambos controllers, sin base
    // compartida en este proyecto).
    private bool TryGetIdempotencyKey(out Guid idempotencyKey, out string errorMessage)
    {
        idempotencyKey = Guid.Empty;
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count == 0)
        {
            errorMessage = "Falta el encabezado Idempotency-Key.";
            return false;
        }
        if (values.Count > 1)
        {
            errorMessage = "Se recibió más de un valor para Idempotency-Key.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(values[0]) || !Guid.TryParse(values[0], out idempotencyKey))
        {
            errorMessage = "Idempotency-Key debe ser un identificador válido.";
            return false;
        }
        errorMessage = string.Empty;
        return true;
    }

    // ── Fase 71.2-E-D: mismo principio ya aplicado a WalletsController.Transferir
    // en 71.2-E-C — la wallet pagadora y el autor se resuelven exclusivamente
    // desde los claims idPersona/idUsuario del JWT, nunca desde el body del
    // cliente. Antes de esta corrección, request.IdWalletUsuario venía directo
    // del cliente sin validar ownership (IDOR de escritura — ver
    // docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md, V18).
    [Authorize(Policy = "KycAprobado")]
    [HttpPost("pagar")]
    public async Task<IActionResult> Pagar([FromBody] PagoQrRequest request)
    {
        if (!TryGetIdPersona(out var idPersona) || !TryGetUsuarioId(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });

        if (!TryGetIdempotencyKey(out var idempotencyKey, out var idempotencyError))
            return BadRequest(new { success = false, message = idempotencyError });

        var walletPropia = await _walletService.ObtenerWalletPersonaAsync(idPersona);
        if (walletPropia == null)
            return NotFound(new { success = false, message = "No se encontró wallet activa para esta persona." });

        _audit.LogSensitiveAction(HttpContext, "QR_PAYMENT_ATTEMPT",
            new { idWalletUsuario = walletPropia.IdWallet, valor = request.Valor });
        try
        {
            var resultado = await _pagoQrService.PagarQrAsync(walletPropia.IdWallet, idUsuario, idempotencyKey, request);
            var data = resultado.Data;
            if (resultado.Replayed)
            {
                _logger.LogInformation(
                    "Pago QR reproducido por idempotencia — idUsuario {IdUsuario}, endpoint {Endpoint}, idempotencyKey {IdempotencyKey}.",
                    idUsuario, "qr.pagar", idempotencyKey);
                Response.Headers["Idempotent-Replayed"] = "true";
            }
            else
            {
                _audit.LogSensitiveAction(HttpContext, "QR_PAYMENT_SUCCESS",
                    new { idVentaQr = data.IdVentaQr, idTransaccion = data.IdTransaccion, idComercio = data.IdComercio, valor = data.Valor });
            }
            return Ok(new
            {
                success = true,
                message = "Pago QR realizado exitosamente.",
                data = new
                {
                    idVentaQr       = data.IdVentaQr,
                    idTransaccion   = data.IdTransaccion,
                    idComercio      = data.IdComercio,
                    idTienda        = data.IdTienda,
                    idWalletUsuario = data.IdWalletUsuario,
                    valor           = data.Valor,
                    estado          = data.Estado
                }
            });
        }
        catch (IdempotencyConflictException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (IdempotencyUnavailableException ex)
        {
            _logger.LogWarning(ex, "Idempotencia no resuelta a tiempo — idUsuario {IdUsuario}, idempotencyKey {IdempotencyKey}.", idUsuario, idempotencyKey);
            return StatusCode(503, new { success = false, message = ex.Message });
        }
        catch (TransientDatabaseException ex)
        {
            _logger.LogWarning(ex, "Conflicto transitorio de base de datos en pago QR — idUsuario {IdUsuario}.", idUsuario);
            return StatusCode(503, new { success = false, message = "El sistema está ocupado procesando otra operación. Intenta de nuevo en unos segundos." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno procesando pago QR para wallet {IdWallet}.", walletPropia.IdWallet);
            return StatusCode(500, new { success = false, message = "Error interno procesando el pago QR." });
        }
    }
}
