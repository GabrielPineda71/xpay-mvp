using Microsoft.AspNetCore.Mvc;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api")]
public class VersionController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration      _config;

    public VersionController(IWebHostEnvironment env, IConfiguration config)
    {
        _env    = env;
        _config = config;
    }

    [HttpGet("version")]
    public IActionResult Version() => Ok(new
    {
        success = true,
        data = new
        {
            name        = _config["Api:Name"]    ?? "XPAY API",
            version     = _config["Api:Version"] ?? "0.1.0-mvp",
            environment = _env.EnvironmentName
        }
    });
}
