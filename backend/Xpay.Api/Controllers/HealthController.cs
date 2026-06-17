using Microsoft.AspNetCore.Mvc;

namespace Xpay.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Health() => Ok(new
    {
        status    = "Healthy",
        service   = "XPAY API",
        timestamp = DateTime.UtcNow
    });
}
