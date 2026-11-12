using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Common;

// PIN transaccional: marca un endpoint de usuario final que mueve dinero.
// Corre después del model binding (y después de [OperacionFinanciera], que es
// filtro de recurso: con el interruptor cerrado se responde 503 sin contar
// intentos). Verifica el PIN del cuerpo (IConPinTransaccional) del usuario del
// token y lo borra del objeto antes de la action.
//
// Fail-closed: sin argumento IConPinTransaccional, sin idUsuario en el token o
// sin PinTransaccionalService registrado, la action no se ejecuta.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequierePinTransaccionalAttribute : Attribute, IAsyncActionFilter
{
    internal const string MensajeRequerido     = "Ingresa tu PIN transaccional de 7 dígitos.";
    internal const string MensajeNoConfigurado = "Debes crear tu PIN transaccional antes de operar.";
    internal const string MensajeBloqueado     = "Tu PIN transaccional está bloqueado por intentos fallidos. Solicita a XPAY su restablecimiento.";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var cuerpo  = context.ActionArguments.Values.OfType<IConPinTransaccional>().FirstOrDefault();
        var servicio = context.HttpContext.RequestServices.GetService(typeof(PinTransaccionalService)) as PinTransaccionalService;
        if (cuerpo is null || servicio is null)
        {
            context.Result = Respuesta(StatusCodes.Status500InternalServerError, "PIN_CONFIGURACION", "Error interno validando el PIN.");
            return;
        }
        if (!long.TryParse(context.HttpContext.User.FindFirst("idUsuario")?.Value, out var idUsuario) || idUsuario <= 0)
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "Token inválido." });
            return;
        }

        var pin = cuerpo.Pin;
        cuerpo.Pin = null;
        var v = await servicio.VerificarAsync(idUsuario, pin, context.HttpContext.RequestAborted);

        var audit = context.HttpContext.RequestServices.GetService(typeof(AuditLogService)) as AuditLogService;
        if (v.Resultado != ResultadoPin.Correcto)
            audit?.LogSensitiveAction(context.HttpContext, "PIN_VERIFICACION_RECHAZADA",
                new { idUsuario, resultado = v.Resultado.ToString(), v.IntentosRestantes });

        context.Result = v.Resultado switch
        {
            ResultadoPin.Correcto        => null,
            ResultadoPin.FormatoInvalido => Respuesta(StatusCodes.Status400BadRequest, "PIN_REQUERIDO", MensajeRequerido),
            // 428 y no 409: el frontend trata 409 como "conflicto de la operación" (p. ej. destinatario cambiado).
            ResultadoPin.NoConfigurado   => Respuesta(StatusCodes.Status428PreconditionRequired, "PIN_NO_CONFIGURADO", MensajeNoConfigurado),
            ResultadoPin.Bloqueado       => Respuesta(StatusCodes.Status423Locked, "PIN_BLOQUEADO", MensajeBloqueado),
            _ => new ObjectResult(new
            {
                success = false, codigo = "PIN_INCORRECTO", intentosRestantes = v.IntentosRestantes,
                message = $"PIN incorrecto. Te quedan {v.IntentosRestantes} intento(s).",
            }) { StatusCode = StatusCodes.Status400BadRequest },
        };
        if (context.Result is null) await next();
    }

    private static ObjectResult Respuesta(int status, string codigo, string mensaje) =>
        new(new { success = false, codigo, message = mensaje }) { StatusCode = status };
}
