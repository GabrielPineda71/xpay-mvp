using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Xpay.Api.Data;
using Xpay.Api.Services;

namespace Xpay.Api.Common;

// Esta entrega autoriza solo la Wallet del autorregistro. El flujo separado
// de revisión laboral/crediticia no está incluido en la activación automática.
// El piloto previo conserva su comportamiento. Nunca se confía en el JWT de
// presentación para transformar una cuenta pública en un alta administrativa.
[AttributeUsage(AttributeTargets.Class)]
public sealed class AltaWalletSinCreditoAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (long.TryParse(context.HttpContext.User.FindFirst("idUsuario")?.Value, out var id) && id > 0)
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<XpayDbContext>();
            if (await RegistroPublicoService.EsAutorregistroAsync(db, id, context.HttpContext.RequestAborted))
            {
                context.Result = new ObjectResult(new { success = false, codigo = "CREDITO_REQUIERE_HABILITACION",
                    message = "Los cupos y anticipos requieren una habilitación separada de XPAY. Puedes usar tu Wallet." })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
        }
        await next();
    }
}
