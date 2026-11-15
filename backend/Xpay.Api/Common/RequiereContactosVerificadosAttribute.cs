using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;

namespace Xpay.Api.Common;

// Requisito de correo y celular verificados para INICIAR operaciones
// financieras de usuario final (anticipo, retiro Bre-B, transferencia, pago QR).
//
// Desactivado por defecto. Solo es efectivo cuando:
//   · XPAY_CONTACTOS_VERIFICADOS_REQUERIDOS es exactamente "true", y
//   · los dos canales (correo y WhatsApp) están configurados.
// Si falta un canal, el requisito no se aplica aunque el interruptor esté en
// "true": nunca se bloquea a un usuario que no tiene cómo verificar su contacto.
//
// Alcance: se declara solo en los endpoints que el usuario inicia. Webhooks,
// conciliación, consulta de saldos, perfil, KYC y la propia verificación de
// contactos no lo llevan (ver ContactosVerificadosArquitecturaTests).
public sealed class RequisitoContactosVerificados(IConfiguration config, IEnumerable<ICanalCodigoContacto> canales)
{
    public const string ClaveInterruptor = "XPAY_CONTACTOS_VERIFICADOS_REQUERIDOS";

    public bool InterruptorActivo =>
        string.Equals(config[ClaveInterruptor]?.Trim(), "true", StringComparison.Ordinal);

    public bool CanalesOperativos =>
        canales.LastOrDefault(c => c.Canal == CanalContacto.Email) is { Disponible: true }
        && canales.LastOrDefault(c => c.Canal == CanalContacto.WhatsApp) is { Disponible: true };

    public bool Efectivo => InterruptorActivo && CanalesOperativos;
}

// Corre antes que [RequierePinTransaccional] (Order menor): sin contactos
// verificados no se consume un intento de PIN.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiereContactosVerificadosAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    internal const string Mensaje =
        "Verifica tu correo y tu celular en Perfil › Mi información antes de realizar esta operación.";

    public int Order => -10;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var servicios = context.HttpContext.RequestServices;
        if (servicios.GetService(typeof(RequisitoContactosVerificados)) is not RequisitoContactosVerificados requisito)
        {
            context.Result = new ObjectResult(new { success = false, codigo = "CONTACTOS_CONFIGURACION", message = "Error interno validando los contactos." })
                { StatusCode = StatusCodes.Status500InternalServerError };
            return;
        }
        if (!requisito.Efectivo)
        {
            await next();
            return;
        }

        if (!long.TryParse(context.HttpContext.User.FindFirst("idUsuario")?.Value, out var idUsuario) || idUsuario <= 0)
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "Token inválido." });
            return;
        }

        var db = (XpayDbContext)servicios.GetService(typeof(XpayDbContext))!;
        var estado = await db.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new { u.EmailVerificado, u.CelularVerificado })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (estado is { EmailVerificado: true, CelularVerificado: true })
        {
            await next();
            return;
        }

        // 428 como PIN_NO_CONFIGURADO: falta una condición previa del usuario.
        context.Result = new ObjectResult(new
        {
            success = false,
            codigo = "CONTACTOS_NO_VERIFICADOS",
            emailVerificado = estado?.EmailVerificado ?? false,
            celularVerificado = estado?.CelularVerificado ?? false,
            message = Mensaje,
        }) { StatusCode = StatusCodes.Status428PreconditionRequired };
    }
}
