using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Xpay.Api.Authorization;

// KYC-GATING-001: intercepta únicamente el resultado 403 causado
// EXCLUSIVAMENTE por KycAprobadoRequirement para devolver un payload
// estructurado que el frontend pueda distinguir de forma inequívoca
// (error="KYC_REQUIRED") de otros 403 (rol insuficiente,
// ClaveVigenteRequirement, etc.). Cualquier otro resultado de autorización
// (éxito, challenge 401, u otro requirement fallido — solo o combinado con
// KYC) se delega sin cambios al handler por defecto de ASP.NET Core — este
// handler nunca reemplaza el comportamiento existente, solo lo amplía para
// este caso puntual.
//
// BREB-COMERCIO-IDOR-FIX-001 dejó ambigüedad pendiente: ClaveVigenteRequirement
// vive en DefaultPolicy y se combina (AND) con TODO endpoint que además
// lleva [Authorize(Policy="KycAprobado")], así que ambos requirements pueden
// fallar en la misma petición. La regla aquí es deliberadamente "todo o
// nada": KYC_REQUIRED solo cuando KycAprobadoRequirement es la ÚNICA causa
// del fallo (FailedRequirements contiene un elemento y es KycAprobadoRequirement).
// Si cualquier otro requirement —presente o futuro— falla junto con KYC, se
// delega íntegro al handler por defecto. No se decide aquí ninguna
// prioridad de negocio entre KYC y otras causas de bloqueo.
public class KycAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var failedRequirements = authorizeResult.Forbidden
            ? authorizeResult.AuthorizationFailure?.FailedRequirements
            : null;

        var failedByKycOnly = failedRequirements is not null
            && failedRequirements.Any()
            && failedRequirements.All(r => r is KycAprobadoRequirement);

        if (!failedByKycOnly)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString() ?? string.Empty
            : string.Empty;

        context.Response.StatusCode  = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            success       = false,
            error         = "KYC_REQUIRED",
            message       = "Esta operación requiere identidad verificada.",
            correlationId
        });

        await context.Response.WriteAsync(body);
    }
}
