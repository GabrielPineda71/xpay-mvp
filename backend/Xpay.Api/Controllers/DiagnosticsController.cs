using Microsoft.AspNetCore.Mvc;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration      _config;

    public DiagnosticsController(IWebHostEnvironment env, IConfiguration config)
    {
        _env    = env;
        _config = config;
    }

    /// <summary>
    /// Diagnóstico básico: responde OK sin exponer secretos ni datos de infraestructura.
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        var correlationId = HttpContext.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString()
            : HttpContext.Response.Headers["X-Correlation-ID"].FirstOrDefault() ?? string.Empty;

        return Ok(new
        {
            status        = "OK",
            service       = _config["Api:Name"] ?? "XPAY API",
            environment   = _env.EnvironmentName,
            timestamp     = DateTime.UtcNow,
            correlationId
        });
    }
}
