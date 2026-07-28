using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// Fase 71.2-E-B: [Authorize] genérico permitía a cualquier autenticado
// (incluido USUARIO_FINAL) listar/operar retiros de cualquier comercio
// cambiando idComercio/idRetiro en la petición (ver
// docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md). Restringido a los 4
// roles con actor legítimo real; ownership fino validado en
// RetiroComercioService, no solo por este atributo.
[ApiController]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY,COMERCIO")]
[Route("api/comercios")]
public class ComerciosController : ControllerBase
{
    private readonly LiquidacionComercioService _liquidacionService;
    private readonly RetiroComercioService      _retiroService;
    private readonly AuditLogService            _audit;
    private readonly ILogger<ComerciosController> _logger;

    public ComerciosController(LiquidacionComercioService liquidacionService, RetiroComercioService retiroService, AuditLogService audit, ILogger<ComerciosController> logger)
    {
        _liquidacionService = liquidacionService;
        _retiroService      = retiroService;
        _audit              = audit;
        _logger             = logger;
    }

    private bool TryGetUsuarioId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    private bool EsAdministrativo =>
        User.IsInRole("ADMIN_XPAY") || User.IsInRole("SUPERUSUARIO") || User.IsInRole("OPERADOR_XPAY");

    [HttpGet("retiros")]
    public async Task<IActionResult> ListarRetiros(
        [FromQuery] string?   estado     = null,
        [FromQuery] long?     idComercio = null,
        [FromQuery] DateTime? desde      = null,
        [FromQuery] DateTime? hasta      = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 20)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await _retiroService.ListarRetirosAsync(estado, idComercio, desde, hasta, page, pageSize, uid, EsAdministrativo);
            return Ok(new { success = true, data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno listando los retiros." }); }
    }

    [HttpGet("retiros/{idRetiro}")]
    public async Task<IActionResult> GetRetiro(long idRetiro)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await _retiroService.GetRetiroByIdAsync(idRetiro, uid, EsAdministrativo);
            return Ok(new { success = true, data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando el retiro." }); }
    }

    // Fase 71.2-E-B: sin consumidor frontend confirmado y sin evidencia de que
    // COMERCIO/OPERADOR_XPAY deban liquidar directamente una venta QR —
    // restringido de forma conservadora a administración. Ver documento de
    // seguridad para el hallazgo y la justificación de esta decisión.
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpPost("liquidar-venta-qr")]
    public async Task<IActionResult> LiquidarVentaQr([FromBody] LiquidarVentaQrRequest request)
    {
        _audit.LogSensitiveAction(HttpContext, "QR_SETTLEMENT_ATTEMPT",
            new { idVentaQr = request.IdVentaQr });
        try
        {
            var liq = await _liquidacionService.LiquidarVentaQrAsync(request);
            _audit.LogSensitiveAction(HttpContext, "QR_SETTLEMENT_SUCCESS",
                new { idLiquidacion = liq.IdLiquidacion, idVentaQr = request.IdVentaQr, idComercio = liq.IdComercio, valorNeto = liq.ValorNeto });
            return Ok(new
            {
                success = true,
                message = "Venta QR liquidada exitosamente.",
                data = new
                {
                    idLiquidacion    = liq.IdLiquidacion,
                    idVentaQr        = request.IdVentaQr,
                    idComercio       = liq.IdComercio,
                    idWalletComercio = liq.IdWalletComercio,
                    valorNeto        = liq.ValorNeto,
                    estadoVenta      = "LIQUIDADA"
                }
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno procesando la liquidación." }); }
    }

    // Fase 71.2-E-B: no se agrega OPERADOR_XPAY — originar una solicitud de
    // retiro no es "gestionar el flujo aprobado", es crear uno nuevo; sin
    // evidencia de que sea parte de su función. request.IdComercio se fuerza
    // desde el scope real cuando el solicitante es COMERCIO (nunca se confía
    // en el valor recibido) — ver RetiroComercioService.SolicitarRetiroAsync.
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,COMERCIO")]
    [HttpPost("solicitar-retiro")]
    public async Task<IActionResult> SolicitarRetiro([FromBody] SolicitarRetiroComercioRequest request)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        // metadata segura: sin NumeroCuenta, TitularCuenta ni DocumentoTitular
        _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_REQUEST_ATTEMPT",
            new { idComercio = request.IdComercio, valor = request.Valor, medioRetiro = request.MedioRetiro });
        try
        {
            var retiro = await _retiroService.SolicitarRetiroAsync(request, uid, EsAdministrativo);
            _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_REQUEST_SUCCESS",
                new { idRetiro = retiro.IdRetiro, idComercio = retiro.IdComercio, valor = retiro.Valor });
            return Ok(new
            {
                success = true,
                message = "Solicitud de retiro creada exitosamente.",
                data = new
                {
                    idRetiro         = retiro.IdRetiro,
                    idComercio       = retiro.IdComercio,
                    idWalletComercio = retiro.IdWalletComercio,
                    valor            = retiro.Valor,
                    estado           = retiro.Estado
                }
            });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno procesando la solicitud de retiro." }); }
    }

    // Fase 71.2-E-B: nunca COMERCIO — un comercio no puede auto-aprobar su
    // propio retiro. OPERADOR_XPAY habilitado explícitamente: coincide con su
    // descripción de rol ("gestión de retiros XPAY", migración 007).
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY")]
    [HttpPost("retiros/confirmar-pago")]
    public async Task<IActionResult> ConfirmarPago([FromBody] ConfirmarRetiroComercioRequest request)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_PAID_ATTEMPT",
            new { idRetiro = request.IdRetiro });
        try
        {
            var retiro = await _retiroService.ConfirmarRetiroPagadoAsync(request, uid);
            _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_PAID_SUCCESS",
                new { idRetiro = retiro.IdRetiro, valor = retiro.Valor });
            return Ok(new
            {
                success = true,
                message = "Retiro marcado como pagado exitosamente.",
                data = new
                {
                    idRetiro = retiro.IdRetiro,
                    estado   = retiro.Estado,
                    valor    = retiro.Valor
                }
            });
        }
        catch (TransicionRetiroInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno confirmando el pago del retiro {IdRetiro}.", request.IdRetiro);
            return StatusCode(500, new { success = false, message = "Error interno confirmando el pago del retiro." });
        }
    }

    // Fase 71.2-E-B: mismo criterio que ConfirmarPago.
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY")]
    [HttpPost("retiros/rechazar")]
    public async Task<IActionResult> RechazarRetiro([FromBody] RechazarRetiroComercioRequest request)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_REJECTED_ATTEMPT",
            new { idRetiro = request.IdRetiro });
        try
        {
            var retiro = await _retiroService.RechazarRetiroAsync(request, uid);
            _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_REJECTED_SUCCESS",
                new { idRetiro = retiro.IdRetiro, valor = retiro.Valor });
            return Ok(new
            {
                success = true,
                message = "Retiro rechazado exitosamente.",
                data = new
                {
                    idRetiro = retiro.IdRetiro,
                    estado   = retiro.Estado,
                    valor    = retiro.Valor
                }
            });
        }
        catch (TransicionRetiroInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno rechazando el retiro {IdRetiro}.", request.IdRetiro);
            return StatusCode(500, new { success = false, message = "Error interno rechazando el retiro." });
        }
    }
}
