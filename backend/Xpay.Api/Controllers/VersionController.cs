using Microsoft.AspNetCore.Mvc;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api")]
public class VersionController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public VersionController(IWebHostEnvironment env) => _env = env;

    [HttpGet("version")]
    public IActionResult Version() => Ok(new
    {
        success = true,
        data = new
        {
            name        = "XPAY API",
            version     = "0.1.0-mvp",
            environment = _env.EnvironmentName
        }
    });
}
