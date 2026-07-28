using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/wallets")]
public class WalletsController : ControllerBase
{
    private readonly WalletService          _walletService;
    private readonly WalletOperacionService _walletOperacionService;
    private readonly AuditLogService        _audit;
    private readonly ILogger<WalletsController> _logger;

    public WalletsController(WalletService walletService, WalletOperacionService walletOperacionService, AuditLogService audit, ILogger<WalletsController> logger)
    {
        _walletService          = walletService;
        _walletOperacionService = walletOperacionService;
        _audit                  = audit;
        _logger                 = logger;
    }

    private bool TryGetIdPersona(out long idPersona) =>
        long.TryParse(User.FindFirst("idPersona")?.Value, out idPersona) && idPersona > 0;

    private bool TryGetUsuarioId(out long idUsuario) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out idUsuario) && idUsuario > 0;

    // Fase 71.2-E-G: Idempotency-Key debe llegar por header, generada por el
    // cliente — nunca generada por el backend cuando falta (eso anularía su
    // propósito). Se valida: presente, un único valor, no vacío, GUID válido.
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

    // ── Fase 71.2-E-B: fuente segura de wallet propia — idPersona viene
    // exclusivamente del claim JWT, nunca de un parámetro del cliente. No
    // requiere rol específico: cualquier autenticado puede consultar la suya.
    // Fase 71.2-E-E: claim inválido → 401 sin log (no es una falla del sistema,
    // es un token ausente/inválido); wallet inexistente → 404 sin log (resultado
    // de negocio esperado, no una excepción); solo la excepción inesperada del
    // catch genérico se registra con LogError.
    [HttpGet("mi-wallet")]
    public async Task<IActionResult> ObtenerMiWallet()
    {
        if (!TryGetIdPersona(out var idPersona))
            return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var wallet = await _walletService.ObtenerWalletPersonaAsync(idPersona);
            if (wallet == null)
                return NotFound(new { success = false, message = "No se encontró wallet activa para esta persona." });
            return Ok(new
            {
                success = true,
                data = new
                {
                    idWallet     = wallet.IdWallet,
                    idPersona    = wallet.IdPersona,
                    nombreWallet = wallet.NombreWallet,
                    estado       = wallet.Estado
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno consultando mi-wallet para idPersona {IdPersona}.", idPersona);
            return StatusCode(500, new { success = false, message = "Error interno consultando mi wallet." });
        }
    }

    // ── Fase 71.2-E-B: uso administrativo — permite consultar la wallet de
    // cualquier persona por id. Restringido a roles administrativos porque no
    // valida ownership (ver docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md).
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpGet("persona/{idPersona:long}")]
    public async Task<IActionResult> ObtenerWalletPersona(long idPersona)
    {
        var wallet = await _walletService.ObtenerWalletPersonaAsync(idPersona);
        return wallet == null ? NotFound(new { success = false, message = "No se encontró wallet activa para esta persona." }) : Ok(new { success = true, data = wallet });
    }

    // ── Fase 71.2-E-B: mismo motivo que ObtenerWalletPersona — administrativo,
    // sin validación de ownership sobre idWallet.
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpGet("{idWallet:long}/saldo")]
    public async Task<IActionResult> ObtenerSaldo(long idWallet)
    {
        var saldo = await _walletService.ObtenerSaldoAsync(idWallet);
        return saldo == null ? NotFound(new { success = false, message = "No se encontró saldo para esta wallet." }) : Ok(new { success = true, data = saldo });
    }

    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpGet("{idWallet:long}/movimientos")]
    public async Task<IActionResult> ObtenerMovimientos(long idWallet) => Ok(new { success = true, data = await _walletService.ObtenerMovimientosAsync(idWallet) });

    // ── Fase 71.2-E-C: auditado — sin consumidor frontend, acredita cualquier
    // wallet sin respaldo de pago real (crea saldo). [Authorize] genérico
    // permitía a cualquier autenticado recargar cualquier wallet arbitraria.
    // Restringido a administración — ver docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md.
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpPost("{idWallet:long}/recarga-manual")]
    public async Task<IActionResult> RecargarManual(long idWallet, [FromBody] RecargaWalletRequest request)
    {
        _audit.LogSensitiveAction(HttpContext, "WALLET_MANUAL_RECHARGE_ATTEMPT",
            new { idWallet, valor = request.Valor });
        try
        {
            var idMovimiento = await _walletOperacionService.RecargarWalletManualAsync(idWallet, request);
            _audit.LogSensitiveAction(HttpContext, "WALLET_MANUAL_RECHARGE_SUCCESS",
                new { idWallet, valor = request.Valor, idMovimiento });
            return Ok(new { success = true, message = "Recarga manual aplicada correctamente.", idMovimientoWallet = idMovimiento });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno aplicando recarga manual a wallet {IdWallet}.", idWallet);
            return StatusCode(500, new { success = false, message = "Error interno aplicando recarga manual." });
        }
    }

    // ── Fase 71.2-E-C: contrato "Mi Wallet" — idWalletOrigen y creadoPor se
    // resuelven exclusivamente desde los claims idPersona/idUsuario del JWT.
    // El cliente solo puede enviar destino, valor y descripción (ver DTO). No
    // existe hoy un flujo administrativo separado que transfiera en nombre de
    // un tercero — único consumidor confirmado es UserWalletPage.tsx.
    [HttpPost("transferencia")]
    public async Task<IActionResult> Transferir([FromBody] TransferenciaWalletRequest request)
    {
        if (!TryGetIdPersona(out var idPersona) || !TryGetUsuarioId(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });

        // Fase 71.2-E-G: se valida antes de tocar la base — un Idempotency-Key
        // ausente/inválido no debe iniciar la operación financiera ni
        // registrarse como falla del sistema.
        if (!TryGetIdempotencyKey(out var idempotencyKey, out var idempotencyError))
            return BadRequest(new { success = false, message = idempotencyError });

        var walletOrigen = await _walletService.ObtenerWalletPersonaAsync(idPersona);
        if (walletOrigen == null)
            return NotFound(new { success = false, message = "No se encontró wallet activa para esta persona." });

        _audit.LogSensitiveAction(HttpContext, "WALLET_TRANSFER_ATTEMPT",
            new { idWalletOrigen = walletOrigen.IdWallet, idWalletDestino = request.IdWalletDestino, valor = request.Valor });
        try
        {
            var resultado = await _walletOperacionService.TransferirWalletAsync(walletOrigen.IdWallet, idUsuario, idempotencyKey, request);
            var data = resultado.Data;
            if (resultado.Replayed)
            {
                _logger.LogInformation(
                    "Transferencia reproducida por idempotencia — idUsuario {IdUsuario}, endpoint {Endpoint}, idempotencyKey {IdempotencyKey}.",
                    idUsuario, "wallets.transferencia", idempotencyKey);
                Response.Headers["Idempotent-Replayed"] = "true";
            }
            else
            {
                _audit.LogSensitiveAction(HttpContext, "WALLET_TRANSFER_SUCCESS",
                    new { idWalletOrigen = data.IdWalletOrigen, idWalletDestino = data.IdWalletDestino, valor = data.Valor, idTransaccion = data.IdTransaccion });
            }
            return Ok(new
            {
                success = true,
                message = "Transferencia realizada exitosamente.",
                data = new
                {
                    idTransaccion   = data.IdTransaccion,
                    idWalletOrigen  = data.IdWalletOrigen,
                    idWalletDestino = data.IdWalletDestino,
                    valor           = data.Valor
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
            _logger.LogWarning(ex, "Conflicto transitorio de base de datos en transferencia — idUsuario {IdUsuario}.", idUsuario);
            return StatusCode(503, new { success = false, message = "El sistema está ocupado procesando otra operación. Intenta de nuevo en unos segundos." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno procesando transferencia desde wallet {IdWalletOrigen}.", walletOrigen.IdWallet);
            return StatusCode(500, new { success = false, message = "Error interno procesando la transferencia." });
        }
    }
}
