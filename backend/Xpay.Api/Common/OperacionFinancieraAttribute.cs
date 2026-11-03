using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Xpay.Api.Common;

// Bloqueo financiero, fase visual: marca un endpoint que mueve dinero, crea
// obligaciones o llama a Passport/servicios de riesgo con efecto. Con el
// interruptor cerrado (ControlOperacionesFinancieras) responde 503 ANTES del
// model binding y de la action: no se lee el body, no se invoca ningún
// servicio y no sale ninguna llamada externa. Mismo patrón que
// SoloAmbienteNoProductivo, después de autenticar y autorizar.
//
// Fail-closed: si ControlOperacionesFinancieras no está registrado, bloquea.
// Todo endpoint de escritura debe llevar este atributo o figurar como no
// financiero en BloqueoFinancieroArquitecturaTests.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OperacionFinancieraAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var control = context.HttpContext.RequestServices.GetService(typeof(ControlOperacionesFinancieras))
            as ControlOperacionesFinancieras;
        if (control is not null && control.EstaHabilitado()) return;

        context.Result = new ObjectResult(new { success = false, message = ControlOperacionesFinancieras.MensajeNoDisponible })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
