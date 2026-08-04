using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// Fase USUARIOS-ADMIN-2: listado y consulta de usuarios internos. Mismo
// patrón de autorización que AdminController (ADMIN_XPAY conservado como
// alias técnico heredado, sin renombrar ni eliminar — decisión de producto
// registrada en el diseño de la fase).
// Fase USUARIOS-ADMIN-3: activar/inactivar/desbloquear. Sigue sin crear,
// editar, restablecer clave ni asignar roles.
[ApiController]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
[Route("api/admin/usuarios")]
public class UsuariosAdminController : ControllerBase
{
    private readonly UsuarioAdminService _usuarioAdminService;
    private readonly AuditLogService     _audit;

    public UsuariosAdminController(UsuarioAdminService usuarioAdminService, AuditLogService audit)
    {
        _usuarioAdminService = usuarioAdminService;
        _audit                = audit;
    }

    private bool TryGetUsuarioId(out long idUsuario) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out idUsuario) && idUsuario > 0;

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? texto           = null,
        [FromQuery] string? estado          = null,
        [FromQuery] string? rol             = null,
        [FromQuery] bool    soloBloqueados  = false,
        [FromQuery] int     page            = 1,
        [FromQuery] int     pageSize        = 20)
    {
        _audit.LogSensitiveAction(HttpContext, "ADMIN_USUARIOS_ACCESS",
            new { texto, estado, rol, soloBloqueados, page, pageSize });
        try
        {
            var data = await _usuarioAdminService.ListarUsuariosAsync(texto, estado, rol, soloBloqueados, page, pageSize);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno listando los usuarios." }); }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detalle(long id)
    {
        _audit.LogSensitiveAction(HttpContext, "ADMIN_USUARIO_DETALLE_ACCESS", new { idUsuario = id });
        try
        {
            var data = await _usuarioAdminService.ObtenerDetalleAsync(id);
            return data == null
                ? NotFound(new { success = false, message = "Usuario no encontrado." })
                : Ok(new { success = true, data });
        }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando el usuario." }); }
    }

    [HttpPost("{id:long}/activar")]
    public async Task<IActionResult> Activar(long id)
    {
        if (!TryGetUsuarioId(out var idAdmin)) return Unauthorized(new { success = false, message = "Token inválido." });
        _audit.LogSensitiveAction(HttpContext, "USUARIO_ACTIVAR_ATTEMPT", new { idUsuario = id });
        try
        {
            var data = await _usuarioAdminService.ActivarAsync(id, idAdmin);
            _audit.LogSensitiveAction(HttpContext, "USUARIO_ACTIVAR_SUCCESS", new { idUsuario = id });
            return Ok(new { success = true, message = "Usuario activado correctamente.", data });
        }
        catch (KeyNotFoundException ex)              { return NotFound(new { success = false, message = ex.Message }); }
        catch (TransicionUsuarioInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex)          { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno activando el usuario." }); }
    }

    [HttpPost("{id:long}/inactivar")]
    public async Task<IActionResult> Inactivar(long id)
    {
        if (!TryGetUsuarioId(out var idAdmin)) return Unauthorized(new { success = false, message = "Token inválido." });
        _audit.LogSensitiveAction(HttpContext, "USUARIO_INACTIVAR_ATTEMPT", new { idUsuario = id });
        try
        {
            var data = await _usuarioAdminService.InactivarAsync(id, idAdmin);
            _audit.LogSensitiveAction(HttpContext, "USUARIO_INACTIVAR_SUCCESS", new { idUsuario = id });
            return Ok(new { success = true, message = "Usuario inactivado correctamente.", data });
        }
        catch (KeyNotFoundException ex)              { return NotFound(new { success = false, message = ex.Message }); }
        catch (TransicionUsuarioInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex)          { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno inactivando el usuario." }); }
    }

    [HttpPost("{id:long}/desbloquear")]
    public async Task<IActionResult> Desbloquear(long id)
    {
        if (!TryGetUsuarioId(out var idAdmin)) return Unauthorized(new { success = false, message = "Token inválido." });
        _audit.LogSensitiveAction(HttpContext, "USUARIO_DESBLOQUEAR_ATTEMPT", new { idUsuario = id });
        try
        {
            var data = await _usuarioAdminService.DesbloquearAsync(id, idAdmin);
            _audit.LogSensitiveAction(HttpContext, "USUARIO_DESBLOQUEAR_SUCCESS", new { idUsuario = id });
            return Ok(new { success = true, message = "Usuario desbloqueado correctamente.", data });
        }
        catch (KeyNotFoundException ex)              { return NotFound(new { success = false, message = ex.Message }); }
        catch (TransicionUsuarioInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex)          { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno desbloqueando el usuario." }); }
    }
}
