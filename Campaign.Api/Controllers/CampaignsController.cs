namespace Campaign.Api.Controllers;

using Campaign.Api.Auth;
using Campaign.Api.Contracts;
using Campaign.Core.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The campaigns an agent can work in. Everything else in the API takes a campaign id from the
/// caller, so without this the agent form would have to ask somebody to paste a GUID.
/// </summary>
[ApiController]
[Authorize(Policy = CampaignPolicies.CanReadCampaigns)]
[Route("api/v1/campaigns")]
[Produces("application/json")]
public sealed class CampaignsController : ControllerBase
{
    private readonly ListCampaigns _campaigns;

    public CampaignsController(ListCampaigns campaigns)
    {
        _campaigns = campaigns;
    }

    /// <summary>Every campaign, oldest first. Example: one weekly campaign around today.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CampaignResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<CampaignResponse>>> List(CancellationToken ct)
    {
        var campaigns = await _campaigns.ExecuteAsync(ct);

        return Ok(campaigns.Select(CampaignResponse.From).ToList());
    }
}
