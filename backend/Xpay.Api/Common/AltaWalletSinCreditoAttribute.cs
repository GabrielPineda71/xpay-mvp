using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Xpay.Api.Data;
using Xpay.Api.Services;
using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Common;

// Activar la Wallet nunca autoriza crédito. Anticipo de Nómina puede reconocer
// un permiso de producto expreso de XPAY; Cartera conserva el cierre público.
// El piloto nunca administrado conserva su flujo previo. Ningún claim de
// presentación transforma una cuenta pública en un alta administrativa.
[AttributeUsage(AttributeTargets.Class)]
public sealed class AltaWalletSinCreditoAttribute(bool anticipoNomina = false) : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (long.TryParse(context.HttpContext.User.FindFirst("idUsuario")?.Value, out var id) && id > 0)
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<XpayDbContext>();
            var ct = context.HttpContext.RequestAborted;
            var bloqueado = anticipoNomina
                ? !await new LibranzaAutorizacionAnticipoService(db).PuedeAccederAsync(id, ct)
                : await RegistroPublicoService.EsAutorregistroAsync(db, id, ct);
            if (bloqueado && !anticipoNomina)
            {
                var services = context.HttpContext.RequestServices;
                var piloto = services.GetService<CarteraPilotoService>();
                var iniciaSolicitud = context.ActionDescriptor.EndpointMetadata
                    .OfType<CarteraPilotoOperacionAttribute>().Any(a => a.Habilitar);
                var config = services.GetService<IConfiguration>();
                // Solo este POST puede incorporar al titular. Su propio filtro
                // valida todos los requisitos y reserva bajo el lock de 50.
                if (iniciaSolicitud && config is not null && MiDecisorProduccionConfig.Habilitado(config))
                    bloqueado = false;
                else if (piloto is not null && await piloto.EsHabilitadoAsync(id, ct))
                    bloqueado = false;
            }
            if (bloqueado)
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
