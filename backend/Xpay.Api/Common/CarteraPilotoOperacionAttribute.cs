using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Xpay.Api.Services;

namespace Xpay.Api.Common;

// La habilitación de Wallet no concede crédito. Solo la solicitud explícita
// incorpora al titular elegible al grupo acotado; aceptar/evaluar exige que ya
// pertenezca a él. La decisión y la materialización siguen en su orquestador.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CarteraPilotoOperacionAttribute(bool habilitar = false)
    : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    public bool Habilitar => habilitar;
    public int Order => -100;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var control = services.GetService<ControlOperacionesFinancieras>();
        if (control is null || !control.EstaHabilitado())
        {
            context.Result = Error(503, "CARTERA_NO_DISPONIBLE", ControlOperacionesFinancieras.MensajeNoDisponible);
            return;
        }
        var ids = context.HttpContext.User.FindAll("idUsuario").ToArray();
        if (ids.Length != 1 || !long.TryParse(ids[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            context.Result = Error(401, "SESION_NO_VALIDA", "Vuelve a ingresar para continuar.");
            return;
        }
        // QA conserva su autorización acotada existente. La incorporación
        // productiva de 50 titulares no sustituye esa compuerta ni necesita
        // que se active una bandera productiva dentro de QA.
        if (UsaCompuertaQa(services.GetService<IAmbienteXpay>()))
        {
            await next();
            return;
        }
        var piloto = services.GetService<CarteraPilotoService>();
        if (piloto is null)
        {
            context.Result = Error(503, "CARTERA_NO_DISPONIBLE", "La solicitud de cupo no está disponible en este momento.");
            return;
        }
        var ct = context.HttpContext.RequestAborted;
        if (habilitar)
        {
            // No reservar una plaza por una petición mal formada. La acción
            // conserva su validación original como segunda barrera.
            var keys = context.HttpContext.Request.Headers["Idempotency-Key"];
            if (keys.Count != 1 || !Guid.TryParse(keys[0], out var key) || key == Guid.Empty)
            {
                context.Result = Error(400, "SOLICITUD_IDENTIFICADOR", "Vuelve a iniciar la solicitud de cupo.");
                return;
            }
            var estado = await piloto.AsegurarHabilitacionAsync(id, ct);
            if (!estado.Habilitado || !estado.Disponible)
            {
                context.Result = Error(409, estado.Estado, estado.Mensaje);
                return;
            }
        }
        else
        {
            var estado = await piloto.ObtenerDisponibilidadAsync(id, ct);
            if (!estado.Habilitado || !estado.Disponible)
            {
                context.Result = Error(409, estado.Estado, estado.Mensaje);
                return;
            }
        }
        await next();
    }

    internal static bool UsaCompuertaQa(IAmbienteXpay? ambiente) =>
        ambiente is { PermiteMecanismosNoProductivos: true };

    private static ObjectResult Error(int status, string codigo, string mensaje) =>
        new(new { success = false, codigo, message = mensaje, error = mensaje }) { StatusCode = status };
}
