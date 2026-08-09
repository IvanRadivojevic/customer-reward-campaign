namespace Campaign.Core.Ports;

using Campaign.Core.Domain;

/// <summary>Filter for listing grants. Every field is optional and they combine with AND.</summary>
public sealed record GrantQuery(
    Guid? CampaignId = null,
    Guid? AgentId = null,
    DateOnly? BusinessDate = null,
    GrantStatus? Status = null);

/// <summary>
/// Everything the four grant use cases need to read and write. The campaign and the agent are read
/// through this port as well, because they are only ever loaded as the context of a grant; that
/// keeps the number of ports at the four the plan defines instead of adding one per table.
/// </summary>
public interface IGrantRepository
{
    Task<Domain.Campaign?> FindCampaignAsync(Guid campaignId, CancellationToken ct);

    Task<Agent?> FindAgentByExternalUserIdAsync(string externalUserId, CancellationToken ct);

    Task<RewardGrant?> FindByIdAsync(Guid grantId, CancellationToken ct);

    /// <summary>P-06: the grant already created for this agent under this idempotency key, if any.</summary>
    Task<RewardGrant?> FindByIdempotencyKeyAsync(Guid agentId, string idempotencyKey, CancellationToken ct);

    /// <summary>P-03: the active grant this customer already has in this campaign, across all agents.</summary>
    Task<RewardGrant?> FindActiveGrantForCustomerAsync(Guid campaignId, string customerExternalId, CancellationToken ct);

    /// <summary>P-02: how many active grants the agent has already made on that business date.</summary>
    Task<int> CountActiveGrantsAsync(Guid agentId, Guid campaignId, DateOnly businessDate, CancellationToken ct);

    Task<IReadOnlyList<RewardGrant>> ListAsync(GrantQuery query, CancellationToken ct);

    Task AddAsync(RewardGrant grant, CancellationToken ct);

    /// <summary>
    /// P-05: voids the grant only if it is still active, and reports whether it did. The
    /// implementation is a conditional update that writes itself immediately, so two parallel voids
    /// of the same grant leave exactly one entry in the audit trail without a second transaction.
    /// </summary>
    Task<bool> TryVoidAsync(
        Guid grantId,
        string voidedByExternalUserId,
        string? reason,
        DateTimeOffset voidedAtUtc,
        CancellationToken ct);
}
