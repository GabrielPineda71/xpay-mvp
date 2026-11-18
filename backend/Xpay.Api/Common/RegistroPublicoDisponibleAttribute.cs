using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Xpay.Api.Services;

namespace Xpay.Api.Common;

// Las rutas de alta solo se exponen en Production cuando el servicio está
// explícitamente habilitado con documentos, correo y modalidad limitada.
// Los mecanismos legacy conservan su atributo SoloAmbienteNoProductivo.
[AttributeUsage(AttributeTargets.Class)]
public sealed class RegistroPublicoDisponibleAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var ambiente = context.HttpContext.RequestServices.GetRequiredService<IAmbienteXpay>();
        if (!ambiente.EsProductivo) return;
        var servicio = context.HttpContext.RequestServices.GetRequiredService<RegistroPublicoService>();
        if (servicio.ObtenerConfiguracion() is not null) return;
        context.Result = new NotFoundObjectResult(new { success = false, message = "Registro no disponible." });
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
