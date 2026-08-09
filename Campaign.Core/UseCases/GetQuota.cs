namespace Campaign.Core.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

public sealed record GetQuotaQuery(Guid CampaignId, string AgentExternalUserId);

public sealed record QuotaView(Guid CampaignId, Guid AgentId, DateOnly BusinessDate, int Used, int Limit);

/// <summary>
/// How much of today's limit the agent has already used. It counts the same active grants P-02
/// counts, so the number the form shows and the number the rule enforces cannot disagree.
/// </summary>
public sealed class GetQuota
{
    private readonly IGrantRepository _grants;
    private readonly BusinessDateProvider _businessDates;

    public GetQuota(IGrantRepository grants, BusinessDateProvider businessDates)
    {
        _grants = grants;
        _businessDates = businessDates;
    }

    public async Task<QuotaView> ExecuteAsync(GetQuotaQuery query, CancellationToken ct)
    {
        // Reading is allowed to a deactivated agent, so the active flag is deliberately not checked
        // here; an unknown subject still fails, because there is no agent to report a quota for.
        var agent = await _grants.FindAgentByExternalUserIdAsync(query.AgentExternalUserId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.AgentNotActive,
                "The token does not belong to a known agent.");

        var campaign = await _grants.FindCampaignAsync(query.CampaignId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.CampaignNotActive,
                "The campaign does not exist.");

        var businessDate = _businessDates.Today();
        var used = await _grants.CountActiveGrantsAsync(agent.Id, campaign.Id, businessDate, ct);

        return new QuotaView(campaign.Id, agent.Id, businessDate, used, campaign.DailyLimitPerAgent);
    }
}
