using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public class UsuariosController : ControllerBase
{
    private readonly RegistroUsuarioFinalService _registroService;
    public UsuariosController(RegistroUsuarioFinalService registroService) => _registroService = registroService;

    [HttpPost("registro-final")]
    public async Task<IActionResult> RegistrarUsuarioFinal([FromBody] RegistroUsuarioFinalRequest request)
    {
        try
        {
            var idUsuario = await _registroService.RegistrarAsync(request);
            return Ok(new { success = true, message = "Usuario final registrado correctamente.", idUsuario });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno registrando usuario final." }); }
    }
}
