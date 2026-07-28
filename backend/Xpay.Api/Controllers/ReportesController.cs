using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reportes")]
public class ReportesController : ControllerBase
{
    private readonly ReportesService _reportes;
    private readonly WalletService   _walletService;
    private readonly AuditLogService _audit;
    private readonly ILogger<ReportesController> _logger;

    public ReportesController(ReportesService reportes, WalletService walletService, AuditLogService audit, ILogger<ReportesController> logger)
    {
        _reportes      = reportes;
        _walletService = walletService;
        _audit         = audit;
        _logger        = logger;
    }

    private bool TryGetIdPersona(out long idPersona) =>
        long.TryParse(User.FindFirst("idPersona")?.Value, out idPersona) && idPersona > 0;

    // ── Fase 71.2-E-B: fuente segura — idWallet se resuelve desde el claim
    // idPersona del solicitante (vía WalletService.ObtenerWalletPersonaAsync,
    // misma fuente que WalletsController.mi-wallet), nunca desde un parámetro
    // de ruta manipulable.
    // Fase 71.2-E-E: claim inválido → 401 sin log; wallet inexistente → 404 sin
    // log; InvalidOperationException (regla de negocio esperada) → 400 sin log;
    // solo la excepción inesperada se registra con LogError.
    [HttpGet("mi-estado-cuenta")]
    public async Task<IActionResult> MiEstadoCuenta()
    {
        if (!TryGetIdPersona(out var idPersona))
            return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var wallet = await _walletService.ObtenerWalletPersonaAsync(idPersona);
            if (wallet == null)
                return NotFound(new { success = false, message = "No se encontró wallet activa para esta persona." });
            var data = await _reportes.GetEstadoCuentaWalletAsync(wallet.IdWallet);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno consultando mi-estado-cuenta para idPersona {IdPersona}.", idPersona);
            return StatusCode(500, new { success = false, message = "Error interno consultando mi estado de cuenta." });
        }
    }

    // ── Fase 71.2-E-B: uso administrativo — permite consultar el estado de
    // cuenta de cualquier wallet por id, sin validar ownership. Restringido.
    // UserWalletPage.tsx todavía consume este endpoint (vía DEMO_MAP) — queda
    // roto hasta migrar a mi-estado-cuenta (pendiente frontend, no resuelto
    // en esta etapa — ver docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md).
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpGet("wallet/{idWallet}/estado-cuenta")]
    public async Task<IActionResult> EstadoCuentaWallet(long idWallet)
    {
        try
        {
            var data = await _reportes.GetEstadoCuentaWalletAsync(idWallet);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando estado de cuenta." }); }
    }

    // ── Fase 71.2-E-B: administrativo — idComercio es libre (no viene de
    // ComercioScopeService). Un endpoint acotado por scope para que el propio
    // comercio consulte su resumen queda como trabajo futuro, no implementado
    // en esta etapa.
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpGet("comercios/{idComercio}/resumen")]
    public async Task<IActionResult> ResumenComercio(long idComercio)
    {
        try
        {
            var data = await _reportes.GetResumenComercioAsync(idComercio);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando resumen de comercio." }); }
    }

    // ── Fase 71.2-E-B: administrativo. No se agrega OPERADOR_XPAY — no hay
    // evidencia de que el detalle de una transacción ledger puntual sea
    // necesario para operar el flujo de retiros (a diferencia del resumen
    // general, sección siguiente).
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpGet("ledger/transaccion/{idTransaccion}")]
    public async Task<IActionResult> LedgerTransaccion(long idTransaccion)
    {
        try
        {
            var data = await _reportes.GetLedgerTransaccionAsync(idTransaccion);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando transacción ledger." }); }
    }

    // ── Fase 71.2-E-B: OPERADOR_XPAY habilitado explícitamente — coincide con
    // su descripción de rol ("reportes... XPAY", migración 007) y con la
    // decisión aprobada para esta etapa.
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY")]
    [HttpGet("operaciones/resumen-general")]
    public async Task<IActionResult> ResumenGeneral()
    {
        _audit.LogSensitiveAction(HttpContext, "ADMIN_REPORT_ACCESS");
        try
        {
            var data = await _reportes.GetResumenGeneralAsync();
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando resumen general." }); }
    }
}
