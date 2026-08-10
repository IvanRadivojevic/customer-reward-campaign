namespace Campaign.Core.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

/// <summary>
/// The purchases of a campaign that matched no active grant. They are reported rather than dropped in
/// silence: a purchase nobody was rewarded for is the interesting half of the report.
/// </summary>
public sealed class GetUnmatchedPurchases
{
    private readonly IReportRepository _reports;
    private readonly IGrantRepository _grants;

    public GetUnmatchedPurchases(IReportRepository reports, IGrantRepository grants)
    {
        _reports = reports;
        _grants = grants;
    }

    public async Task<IReadOnlyList<PurchaseResult>> ExecuteAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await _grants.FindCampaignAsync(campaignId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.CampaignNotActive,
                "The campaign does not exist.");

        return await _reports.ListUnmatchedAsync(campaign.Id, ct);
    }
}
