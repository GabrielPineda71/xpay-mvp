using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// XPAY-669 — endpoint administrativo QA para preparar UNA persona de prueba de
// proveedor externo (primera consulta MiDecisor). Separado de KycController:
// no toca simular-estado ni el flujo Veriff. Fail-closed por configuración
// (ver QaIdentidadPruebaConfig): deshabilitado → 404, como si no existiera.
// Sólo recibe el id interno del usuario destino; nunca PII en ruta, body,
// respuesta ni logs.
// XPAY-717 - inexistente (404) en cualquier ambiente productivo, aunque haya flags QA.
[SoloAmbienteNoProductivo]
[ApiController]
[Route("api/qa/identidad-prueba")]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
public sealed class QaIdentidadPruebaController(
    QaIdentidadPruebaService servicio,
    ILogger<QaIdentidadPruebaController> logger) : ControllerBase
{
    [HttpPost("{idUsuario:long}/preparar")]
    public async Task<IActionResult> Preparar(long idUsuario)
    {
        var idAdmin = long.TryParse(User.FindFirst("idUsuario")?.Value, out var id) ? id : 0;

        var resultado = await servicio.PrepararAsync(idUsuario, idAdmin, HttpContext.RequestAborted);

        logger.LogInformation(
            "qa.identidad-prueba: preparar resultado={Resultado} idUsuarioDestino={IdUsuarioDestino} idAdmin={IdAdmin}.",
            resultado, idUsuario, idAdmin);

        return MapearRespuesta(idUsuario, resultado);
    }

    internal static IActionResult MapearRespuesta(long idUsuario, ResultadoPreparacionIdentidadQa resultado) => resultado switch
    {
        ResultadoPreparacionIdentidadQa.NoDisponible =>
            new NotFoundResult(),
        ResultadoPreparacionIdentidadQa.NoAutorizado or ResultadoPreparacionIdentidadQa.AutoPreparacionProhibida =>
            new ObjectResult(new { error = "Operación no autorizada." }) { StatusCode = StatusCodes.Status403Forbidden },
        ResultadoPreparacionIdentidadQa.NoEncontrado =>
            new NotFoundObjectResult(new { error = "Usuario no encontrado." }),
        ResultadoPreparacionIdentidadQa.IdentidadProtegida =>
            new ConflictObjectResult(new { error = "La identidad de este usuario no puede prepararse." }),
        ResultadoPreparacionIdentidadQa.PrecondicionesNoCumplidas =>
            new ConflictObjectResult(new { error = "El usuario no cumple las condiciones para la preparación." }),
        ResultadoPreparacionIdentidadQa.EstadoCambiado =>
            new ConflictObjectResult(new { error = "El estado del usuario cambió; no se aplicó ningún cambio." }),
        ResultadoPreparacionIdentidadQa.YaPreparada =>
            new OkObjectResult(new { idUsuario, resultado = "ya_preparada" }),
        ResultadoPreparacionIdentidadQa.Preparada =>
            new OkObjectResult(new { idUsuario, resultado = "preparada" }),
        _ => new StatusCodeResult(StatusCodes.Status500InternalServerError),
    };
}
