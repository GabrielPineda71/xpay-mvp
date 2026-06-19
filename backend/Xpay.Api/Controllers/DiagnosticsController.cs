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

    // Endpoint diagnóstico para validar ErrorHandlingMiddleware en CI/QA.
    // En producción: Diagnostics__EnableErrorTestEndpoint=false.
    [HttpGet("error-test")]
    public IActionResult ErrorTest()
    {
        var enabled = _config.GetValue("Diagnostics:EnableErrorTestEndpoint", defaultValue: false);
        if (!enabled)
            return NotFound(new { success = false, message = "Not found." });

        throw new InvalidOperationException("Intentional diagnostics error test.");
    }
}
