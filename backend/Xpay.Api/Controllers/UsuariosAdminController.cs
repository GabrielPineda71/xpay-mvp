using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// Fase USUARIOS-ADMIN-2: listado y consulta de usuarios internos. Mismo
// patrón de autorización que AdminController (ADMIN_XPAY conservado como
// alias técnico heredado, sin renombrar ni eliminar — decisión de producto
// registrada en el diseño de la fase). Solo lectura: no crea, edita,
// activa, inactiva, desbloquea, restablece clave ni asigna roles.
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
}
