namespace Campaign.Core.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

public sealed record ListGrantsQuery(
    string ActorExternalUserId,
    bool ActorIsAdmin,
    Guid? CampaignId = null,
    Guid? AgentId = null,
    DateOnly? BusinessDate = null,
    GrantStatus? Status = null);

/// <summary>
/// Lists grants. An admin sees everything; an agent sees their own records, which is the record
/// ownership rule of the CRM expressed in one place instead of in every query.
/// </summary>
public sealed class ListGrants
{
    private readonly IGrantRepository _grants;

    public ListGrants(IGrantRepository grants)
    {
        _grants = grants;
    }

    public async Task<IReadOnlyList<RewardGrant>> ExecuteAsync(ListGrantsQuery query, CancellationToken ct)
    {
        var agentId = query.AgentId;

        if (!query.ActorIsAdmin)
        {
            // Reading is allowed to a deactivated agent, so the active flag is deliberately not
            // checked here; an unknown subject still fails, because there is nothing to scope to.
            var agent = await _grants.FindAgentByExternalUserIdAsync(query.ActorExternalUserId, ct)
                ?? throw new DomainRuleViolationException(
                    DomainErrorCodes.AgentNotActive,
                    "The token does not belong to a known agent.");

            if (agentId is not null && agentId != agent.Id)
            {
                throw new DomainRuleViolationException(
                    DomainErrorCodes.ForbiddenAgentScope,
                    "An agent can only list their own grants.");
            }

            agentId = agent.Id;
        }

        return await _grants.ListAsync(
            new GrantQuery(query.CampaignId, agentId, query.BusinessDate, query.Status),
            ct);
    }
}
