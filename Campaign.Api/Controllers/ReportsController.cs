namespace Campaign.Api.Controllers;

using Campaign.Api.Auth;
using Campaign.Api.Contracts;
using Campaign.Core.Domain;
using Campaign.Core.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize(Policy = CampaignPolicies.CanViewReports)]
[Route("api/v1/campaigns")]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    private readonly GetCampaignResults _results;
    private readonly GetUnmatchedPurchases _unmatched;

    public ReportsController(GetCampaignResults results, GetUnmatchedPurchases unmatched)
    {
        _results = results;
        _unmatched = unmatched;
    }

    /// <summary>The results of a campaign, grouped by agent or by business date.</summary>
    /// <remarks>
    /// The numbers come from the vw_CampaignResults view rather than from a query written here, so a
    /// reporting tool reading the same view gets the same answer. Conversion is converted grants over
    /// active grants: a customer who bought three times converted one grant, and those three
    /// purchases are reported as matchedRows.
    /// </remarks>
    [HttpGet("{campaignId:guid}/results")]
    [ProducesResponseType(typeof(CampaignResultsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CampaignResultsResponse>> Results(
        Guid campaignId,
        [FromQuery] string groupBy,
        CancellationToken ct)
    {
        var view = await _results.ExecuteAsync(campaignId, Grouping(groupBy), ct);

        return Ok(CampaignResultsResponse.From(view));
    }

    /// <summary>The purchases of this campaign that matched no active grant.</summary>
    [HttpGet("{campaignId:guid}/unmatched-purchases")]
    [ProducesResponseType(typeof(IReadOnlyList<PurchaseRowResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<PurchaseRowResponse>>> Unmatched(
        Guid campaignId,
        CancellationToken ct)
    {
        var rows = await _unmatched.ExecuteAsync(campaignId, ct);

        return Ok(rows.Select(PurchaseRowResponse.From).ToList());
    }

    private static ResultsGrouping Grouping(string? groupBy) => groupBy?.ToLowerInvariant() switch
    {
        "agent" => ResultsGrouping.Agent,
        "day" => ResultsGrouping.Day,
        _ => throw new DomainRuleViolationException(
            DomainErrorCodes.ValidationFailed,
            "groupBy has to be either 'agent' or 'day'.")
    };
}
