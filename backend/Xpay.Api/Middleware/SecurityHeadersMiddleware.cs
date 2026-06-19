namespace Xpay.Api.Middleware;

/// <summary>
/// Agrega headers básicos de seguridad HTTP a todas las respuestas.
/// No agrega CSP (pendiente — puede romper Swagger UI) ni HSTS (pendiente — requiere HTTPS productivo).
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool            _enableNoStoreCache;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next               = next;
        _enableNoStoreCache = config.GetValue("SecurityHeaders:EnableNoStoreCache", defaultValue: true);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"]           = "nosniff";
            headers["X-Frame-Options"]                   = "DENY";
            headers["Referrer-Policy"]                   = "no-referrer";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            headers["Permissions-Policy"]                = "camera=(), microphone=(), geolocation=()";

            if (_enableNoStoreCache)
            {
                headers["Cache-Control"] = "no-store, no-cache";
                headers["Pragma"]        = "no-cache";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
