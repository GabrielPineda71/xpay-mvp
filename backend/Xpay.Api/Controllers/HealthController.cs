using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Xpay.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    [AllowAnonymous]
    public IActionResult Health() => Ok(new
    {
        status    = "Healthy",
        service   = "XPAY API",
        timestamp = DateTime.UtcNow
    });
}
