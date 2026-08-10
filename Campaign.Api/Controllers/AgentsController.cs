namespace Campaign.Api.Controllers;

using Campaign.Api.Auth;
using Campaign.Api.Contracts;
using Campaign.Core.UseCases;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/agents")]
[Produces("application/json")]
public sealed class AgentsController : ControllerBase
{
    private readonly GetQuota _getQuota;
    private readonly ICallerContext _caller;

    public AgentsController(GetQuota getQuota, ICallerContext caller)
    {
        _getQuota = getQuota;
        _caller = caller;
    }

    /// <summary>
    /// How much of today's limit the calling agent has already used in a campaign. It counts the same
    /// active grants the rule counts, so the number on the form and the number the rule enforces
    /// cannot disagree.
    /// </summary>
    [HttpGet("me/quota")]
    [ProducesResponseType(typeof(QuotaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuotaResponse>> Quota([FromQuery] Guid campaignId, CancellationToken ct)
    {
        var quota = await _getQuota.ExecuteAsync(
            new GetQuotaQuery(campaignId, _caller.ExternalUserId),
            ct);

        return Ok(QuotaResponse.From(quota));
    }
}
