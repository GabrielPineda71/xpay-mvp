using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/libranza/admin/autorizaciones-anticipo")]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
public sealed class LibranzaAutorizacionAnticipoController(LibranzaAutorizacionAnticipoService servicio) : ControllerBase
{
    private bool IdAdmin(out long id) => long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] long? idConvenio, [FromQuery] int pagina = 1,
        [FromQuery] int tamanio = 50, CancellationToken ct = default)
    {
        if (!IdAdmin(out var id)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await servicio.ListarAsync(id, idConvenio, pagina, tamanio, ct) }); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { success = false, message = "Se requiere un administrador activo de XPAY." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (OverflowException) { return BadRequest(new { success = false, message = "Página inválida." }); }
    }

    [HttpPost("autorizar")]
    public Task<IActionResult> Autorizar([FromBody] CambiarAutorizacionAnticipoRequest solicitud, CancellationToken ct) => Cambiar(solicitud, true, ct);

    [HttpPost("revocar")]
    public Task<IActionResult> Revocar([FromBody] CambiarAutorizacionAnticipoRequest solicitud, CancellationToken ct) => Cambiar(solicitud, false, ct);

    private async Task<IActionResult> Cambiar(CambiarAutorizacionAnticipoRequest solicitud, bool autorizar, CancellationToken ct)
    {
        if (!IdAdmin(out var id)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await servicio.CambiarAsync(id, solicitud, autorizar, ct) }); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { success = false, message = "Se requiere un administrador activo de XPAY." }); }
        catch (AutorizacionAnticipoConflictoException ex) { return Conflict(new { success = false, codigo = "AUTORIZACION_ANTICIPO_DESACTUALIZADA", message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }
}
