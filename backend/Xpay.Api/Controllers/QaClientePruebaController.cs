using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// XPAY-702 - alta de cliente de prueba QA. Solo ADMIN_XPAY / SUPERUSUARIO.
// Sin las banderas QA (ver QaClientePruebaConfig) responde 404, como si no
// existiera. Auditoria sin PII.
// XPAY-717 - inexistente (404) en cualquier ambiente productivo, aunque haya flags QA.
[SoloAmbienteNoProductivo]
[ApiController]
[Route("api/qa/clientes-prueba")]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
public sealed class QaClientePruebaController(
    QaClientePruebaService servicio, AuditLogService audit) : ControllerBase
{
    private long IdUsuarioActual => long.Parse(User.FindFirst("idUsuario")?.Value ?? "0");

    [HttpGet("habilitado")]
    public IActionResult Habilitado()
        => servicio.Habilitado ? Ok(new { habilitado = true }) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] RegistroUsuarioFinalRequest request)
    {
        if (!servicio.Habilitado)
            return NotFound();

        var idAdmin = IdUsuarioActual;
        var r = await servicio.CrearAsync(request, idAdmin, HttpContext.RequestAborted);

        audit.LogSensitiveAction(HttpContext, "QA_CREAR_CLIENTE_PRUEBA",
            new { idAdmin, resultado = r.Resultado.ToString(), idUsuario = r.IdUsuario });

        return r.Resultado switch
        {
            ResultadoClientePruebaQa.Creado          => Ok(new { idUsuario = r.IdUsuario, procedencia = "SIMULACION_QA" }),
            ResultadoClientePruebaQa.NoDisponible    => NotFound(),
            ResultadoClientePruebaQa.NoAutorizado    => Forbid(),
            ResultadoClientePruebaQa.DatosInvalidos  => BadRequest(new { error = r.Mensaje }),
            ResultadoClientePruebaQa.LimiteAlcanzado => Conflict(new { error = "Se alcanzó el máximo de clientes de prueba QA." }),
            ResultadoClientePruebaQa.Conflicto       => Conflict(new { error = r.Mensaje }),
            _                                        => Conflict(new { error = "No fue posible preparar la identidad QA." }),
        };
    }
}
