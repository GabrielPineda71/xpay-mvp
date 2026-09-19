using Microsoft.AspNetCore.Authorization;
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
    private readonly PerfilService _perfilService;
    private readonly ILogger<UsuariosController> _logger;
    public UsuariosController(
        RegistroUsuarioFinalService registroService,
        RegistroInicialService registroInicialService,
        PerfilService perfilService,
        ILogger<UsuariosController> logger)
    {
        _registroService         = registroService;
        _registroInicialService  = registroInicialService;
        _perfilService           = perfilService;
        _logger                  = logger;
    }

    // XPAY-399 — misma fuente/patrón que WalletsController/ReportesController:
    // idPersona/idUsuario se resuelven EXCLUSIVAMENTE desde los claims del
    // JWT del solicitante — nunca desde un parámetro de ruta/body/query. No
    // existe ningún camino en este controller para que un cliente indique
    // "otra" persona/usuario a leer o modificar (protección IDOR).
    private bool TryGetIdPersona(out long idPersona) =>
        long.TryParse(User.FindFirst("idPersona")?.Value, out idPersona) && idPersona > 0;

    private bool TryGetIdUsuario(out long idUsuario) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out idUsuario) && idUsuario > 0;

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

    // XPAY-399 (Perfil Fase 2A) — GET de MI perfil. [Authorize] explícito en
    // el método (esta clase no tiene [Authorize] a nivel de clase porque
    // registro-final/registro-inicial son deliberadamente anónimos).
    [HttpGet("mi-perfil")]
    [Authorize]
    public async Task<IActionResult> ObtenerMiPerfil()
    {
        if (!TryGetIdPersona(out var idPersona))
            return Unauthorized(new { success = false, message = "Token inválido." });

        try
        {
            var data = await _perfilService.ObtenerAsync(idPersona);
            return Ok(new { success = true, data });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno obteniendo mi-perfil.");
            return StatusCode(500, new { success = false, message = "Error interno obteniendo el perfil." });
        }
    }

    // XPAY-399 (Perfil Fase 2A) — PATCH de MI perfil. Solo celular/email/
    // direccion/ciudad/departamento (ver ActualizarMiPerfilRequest/
    // PerfilService) — identidad legal y campos KYC/seguridad nunca pasan
    // por este endpoint, sin importar lo que el cliente envíe (el DTO no
    // tiene esas propiedades: overposting estructuralmente imposible).
    [HttpPatch("mi-perfil")]
    [Authorize]
    public async Task<IActionResult> ActualizarMiPerfil([FromBody] ActualizarMiPerfilRequest request)
    {
        if (!TryGetIdPersona(out var idPersona) || !TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });

        try
        {
            var data = await _perfilService.ActualizarAsync(idUsuario, idPersona, request);
            return Ok(new { success = true, message = "Perfil actualizado correctamente.", data });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno actualizando mi-perfil.");
            return StatusCode(500, new { success = false, message = "Error interno actualizando el perfil." });
        }
    }
}
