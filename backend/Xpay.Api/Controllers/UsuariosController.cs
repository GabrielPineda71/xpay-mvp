using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public class UsuariosController : ControllerBase
{
    private readonly RegistroUsuarioFinalService _registroService;
    private readonly RegistroInicialService _registroInicialService;
    public UsuariosController(RegistroUsuarioFinalService registroService, RegistroInicialService registroInicialService)
    {
        _registroService         = registroService;
        _registroInicialService  = registroInicialService;
    }

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

    // Commit 3 — registro-inicial (Opción B): solo usuario+clave+celular,
    // login inmediato después. Sin [Authorize] a nivel de clase ni de este
    // método — mismo patrón exacto que registro-final (endpoint público de
    // autoregistro), sin necesidad de [AllowAnonymous] adicional.
    [HttpPost("registro-inicial")]
    public async Task<IActionResult> RegistrarInicial([FromBody] RegistroInicialRequest request)
    {
        try
        {
            var data = await _registroInicialService.RegistrarAsync(request);
            return StatusCode(201, new { success = true, message = "Registro inicial exitoso.", data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno en el registro inicial." }); }
    }
}
