namespace Campaign.Tests.Fakes;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

/// <summary>
/// In-memory stand-in for the real repository. It enforces the same reads the database enforces
/// with its indexes, so the use case tests exercise the rules and not the storage technology.
/// </summary>
public sealed class FakeGrantRepository : IGrantRepository
{
    private readonly Dictionary<Guid, Campaign> _campaigns = [];
    private readonly Dictionary<string, Agent> _agents = [];

    internal List<RewardGrant> Grants { get; } = [];

    /// <summary>Adding a campaign that is already there replaces it, which is how a test changes it later.</summary>
    public FakeGrantRepository WithCampaign(Campaign campaign)
    {
        _campaigns[campaign.Id] = campaign;
        return this;
    }

    public FakeGrantRepository WithAgent(Agent agent)
    {
        _agents[agent.ExternalUserId] = agent;
        return this;
    }

    public Task<Campaign?> FindCampaignAsync(Guid campaignId, CancellationToken ct) =>
        Task.FromResult(_campaigns.GetValueOrDefault(campaignId));

    public Task<Agent?> FindAgentByExternalUserIdAsync(string externalUserId, CancellationToken ct) =>
        Task.FromResult(_agents.GetValueOrDefault(externalUserId));

    public Task<RewardGrant?> FindByIdAsync(Guid grantId, CancellationToken ct) =>
        Task.FromResult(Grants.SingleOrDefault(grant => grant.Id == grantId));

    public Task<RewardGrant?> FindByIdempotencyKeyAsync(Guid agentId, string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(Grants.SingleOrDefault(grant =>
            grant.AgentId == agentId
            && string.Equals(grant.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)));

    public Task<RewardGrant?> FindActiveGrantForCustomerAsync(
        Guid campaignId,
        string customerExternalId,
        CancellationToken ct) =>
        Task.FromResult(Grants.SingleOrDefault(grant =>
            grant.CampaignId == campaignId
            && string.Equals(grant.CustomerExternalId, customerExternalId, StringComparison.Ordinal)
            && grant.IsActive));

    /// <summary>Nothing to lock in memory: these tests are about the rules, not about contention.</summary>
    public Task LockAgentAsync(Guid agentId, CancellationToken ct) => Task.CompletedTask;

    public Task<int> CountActiveGrantsAsync(Guid agentId, Guid campaignId, DateOnly businessDate, CancellationToken ct) =>
        Task.FromResult(Grants.Count(grant =>
            grant.AgentId == agentId
            && grant.CampaignId == campaignId
            && grant.BusinessDate == businessDate
            && grant.IsActive));

    public Task<IReadOnlyList<RewardGrant>> ListAsync(GrantQuery query, CancellationToken ct)
    {
        IEnumerable<RewardGrant> matches = Grants;

        if (query.CampaignId is { } campaignId)
        {
            matches = matches.Where(grant => grant.CampaignId == campaignId);
        }

        if (query.AgentId is { } agentId)
        {
            matches = matches.Where(grant => grant.AgentId == agentId);
        }

        if (query.BusinessDate is { } businessDate)
        {
            matches = matches.Where(grant => grant.BusinessDate == businessDate);
        }

        if (query.Status is { } status)
        {
            matches = matches.Where(grant => grant.Status == status);
        }

        return Task.FromResult<IReadOnlyList<RewardGrant>>(
            matches.OrderBy(grant => grant.GrantedAtUtc).ToList());
    }

    public Task AddAsync(RewardGrant grant, CancellationToken ct)
    {
        Grants.Add(grant);
        return Task.CompletedTask;
    }

    public Task<bool> TryVoidAsync(
        Guid grantId,
        string voidedByExternalUserId,
        string? reason,
        DateTimeOffset voidedAtUtc,
        CancellationToken ct)
    {
        // The same condition the real conditional update carries in its WHERE clause.
        var grant = Grants.SingleOrDefault(candidate => candidate.Id == grantId && candidate.IsActive);
        if (grant is null)
        {
            return Task.FromResult(false);
        }

        grant.Void(voidedByExternalUserId, reason, voidedAtUtc);
        return Task.FromResult(true);
    }
}
