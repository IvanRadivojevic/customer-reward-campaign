namespace Campaign.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Liveness only, and deliberately outside /api/v1: it answers whether the process is up, not
/// whether the database is reachable, so it stays useful while the database is the thing that broke.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new { status = "healthy" });
}
