using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Xpay.Api.Data;
using Xpay.Api.Services;

namespace Xpay.Api.Common;

// El correo activa el acceso al perfil, no las operaciones financieras.
// Se relee el marcador persistido en cada petición autenticada, con independencia
// de ambiente, roles, menús o claims de presentación. Una cuenta del piloto sin
// ese marcador conserva sus permisos y controles actuales.
public sealed class CuentaLimitadaFilter(XpayDbContext db, ILogger<CuentaLimitadaFilter> logger)
    : IAsyncResourceFilter, IOrderedFilter
{
    public const int Orden = -2000;
    public const string CodigoCuentaLimitada = "CUENTA_LIMITADA";
    public const string MensajeCuentaLimitada =
        "Tu cuenta está en proceso de habilitación. Puedes consultar y actualizar tu perfil.";
    internal const string MensajeNoDisponible = "No pudimos comprobar los permisos de la cuenta. Intenta de nuevo.";

    // Antes del model binding y de los servicios de la acción. La autenticación
    // y las políticas de autorización mantienen su posición previa en MVC.
    public int Order => Orden;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        var anonima = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()
            || action?.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), inherit: true) == true
            || action?.ControllerTypeInfo.IsDefined(typeof(AllowAnonymousAttribute), inherit: true) == true;

        // Login, enlaces de correo y callbacks conservan su seguridad propia.
        // No se intenta convertir a su remitente externo en un usuario XPAY.
        if (anonima || context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var idsUsuario = context.HttpContext.User.FindAll("idUsuario").ToArray();
        if (idsUsuario.Length != 1
            || !long.TryParse(idsUsuario[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var idUsuario)
            || idUsuario <= 0
            || idUsuario.ToString(CultureInfo.InvariantCulture) != idsUsuario[0].Value)
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "Token inválido." });
            return;
        }

        bool limitada;
        try
        {
            limitada = await RegistroPublicoService.EsCuentaLimitadaAsync(db, idUsuario);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No interpretar una caída de SQL como ausencia de restricciones.
            logger.LogError("No se pudo comprobar la habilitación de la cuenta ({TipoError}).", ex.GetType().Name);
            context.Result = new ObjectResult(new { success = false, message = MensajeNoDisponible })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
            };
            return;
        }

        if (!limitada || action?.MethodInfo.IsDefined(typeof(CuentaLimitadaPermitidaAttribute), inherit: true) == true)
        {
            await next();
            return;
        }

        context.Result = new ObjectResult(new
        {
            success = false,
            codigo = CodigoCuentaLimitada,
            message = MensajeCuentaLimitada,
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
