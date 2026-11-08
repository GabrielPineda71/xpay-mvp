using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Xpay.Api.Exceptions;

namespace Xpay.Api.Common;

// Filtro global: una OperacionFinancieraBloqueadaException que llega sin
// capturar a MVC (p. ej. lanzada por BloqueoFinancieroInterceptor en un flujo
// sin [OperacionFinanciera]) se responde 503 con el mensaje genérico, en vez
// de un 500. Los controladores que ya la capturan (retiro real, anticipos)
// conservan su propia respuesta.
public sealed class OperacionFinancieraBloqueadaFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not OperacionFinancieraBloqueadaException ex) return;
        context.Result = new ObjectResult(new { success = false, message = ex.Message })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
        context.ExceptionHandled = true;
    }
}
