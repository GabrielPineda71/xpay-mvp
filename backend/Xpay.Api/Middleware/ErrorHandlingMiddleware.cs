namespace Xpay.Api.Middleware;

// Captura excepciones no controladas y devuelve JSON seguro (sin stack trace, sin exception.Message).
// Loguea internamente con correlationId para trazabilidad. No modifica errores ya controlados (400/401/404).
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate                    _next;
    private readonly ILogger<ErrorHandlingMiddleware>   _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items.TryGetValue("CorrelationId", out var cid)
                ? cid?.ToString() ?? string.Empty
                : string.Empty;

            _logger.LogError(ex,
                "Unhandled exception | correlationId={CorrelationId} | path={Path} | method={Method}",
                correlationId,
                context.Request.Path.Value,
                context.Request.Method);

            if (context.Response.HasStarted)
            {
                // No se puede escribir nueva respuesta; relanzar para que ASP.NET Core lo gestione
                throw;
            }

            context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(
                $"{{\"success\":false,\"error\":\"internal_server_error\",\"message\":\"An unexpected error occurred.\",\"correlationId\":\"{correlationId}\"}}");
        }
    }
}
