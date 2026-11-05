using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Xpay.Api.Common;

// XPAY-717 — bloquea endpoints QA/demo/diagnóstico/simulación en cualquier
// ambiente productivo (ver AmbienteXpay). Se ejecuta como resource filter:
// antes del model binding y de la action, por lo que en productivo no se
// lee el body ni se invoca ningún servicio. Responde 404 para que el
// endpoint no sea distinguible de una ruta inexistente.
//
// Fail-closed: si IAmbienteXpay no está registrado en DI, también bloquea.
// No consulta configuración: ningún flag puede reabrir el endpoint en
// productivo. Dentro de un ambiente no productivo cada endpoint conserva sus
// propias restricciones (roles, flags XPAY_QA_*, etc.).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SoloAmbienteNoProductivoAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var ambiente = context.HttpContext.RequestServices.GetService(typeof(IAmbienteXpay)) as IAmbienteXpay;
        if (ambiente is null || !ambiente.PermiteMecanismosNoProductivos)
            context.Result = new NotFoundObjectResult(new { success = false, message = "Not found." });
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
