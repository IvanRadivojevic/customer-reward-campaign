namespace Campaign.Infrastructure.Persistence;

using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Microsoft.EntityFrameworkCore;

public sealed class EfGrantRepository : IGrantRepository
{
    private readonly AppDbContext _db;

    public EfGrantRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Campaign?> FindCampaignAsync(Guid campaignId, CancellationToken ct) =>
        _db.Campaigns.FirstOrDefaultAsync(campaign => campaign.Id == campaignId, ct);

    public Task<Agent?> FindAgentByExternalUserIdAsync(string externalUserId, CancellationToken ct) =>
        _db.Agents.FirstOrDefaultAsync(agent => agent.ExternalUserId == externalUserId, ct);

    public Task<RewardGrant?> FindByIdAsync(Guid grantId, CancellationToken ct) =>
        _db.RewardGrants.FirstOrDefaultAsync(grant => grant.Id == grantId, ct);

    public Task<RewardGrant?> FindByIdempotencyKeyAsync(Guid agentId, string idempotencyKey, CancellationToken ct) =>
        _db.RewardGrants.FirstOrDefaultAsync(
            grant => grant.AgentId == agentId && grant.IdempotencyKey == idempotencyKey,
            ct);

    public Task<RewardGrant?> FindActiveGrantForCustomerAsync(
        Guid campaignId,
        string customerExternalId,
        CancellationToken ct) =>
        _db.RewardGrants.FirstOrDefaultAsync(
            grant => grant.CampaignId == campaignId
                && grant.CustomerExternalId == customerExternalId
                && grant.Status == GrantStatus.Active,
            ct);

    public Task<int> CountActiveGrantsAsync(Guid agentId, Guid campaignId, DateOnly businessDate, CancellationToken ct) =>
        _db.RewardGrants.CountAsync(
            grant => grant.AgentId == agentId
                && grant.CampaignId == campaignId
                && grant.BusinessDate == businessDate
                && grant.Status == GrantStatus.Active,
            ct);

    public async Task<IReadOnlyList<RewardGrant>> ListAsync(GrantQuery query, CancellationToken ct)
    {
        var grants = _db.RewardGrants.AsQueryable();

        if (query.CampaignId is { } campaignId)
        {
            grants = grants.Where(grant => grant.CampaignId == campaignId);
        }

        if (query.AgentId is { } agentId)
        {
            grants = grants.Where(grant => grant.AgentId == agentId);
        }

        if (query.BusinessDate is { } businessDate)
        {
            grants = grants.Where(grant => grant.BusinessDate == businessDate);
        }

        if (query.Status is { } status)
        {
            grants = grants.Where(grant => grant.Status == status);
        }

        return await grants.OrderBy(grant => grant.GrantedAtUtc).ToListAsync(ct);
    }

    public async Task AddAsync(RewardGrant grant, CancellationToken ct) =>
        await _db.RewardGrants.AddAsync(grant, ct);

    /// <summary>
    /// P-05 as a single conditional statement. The WHERE clause is the rule, so two parallel voids
    /// of the same grant produce one update and one "already voided" answer, without a second
    /// transaction and without a concurrency token on the row.
    /// </summary>
    public async Task<bool> TryVoidAsync(
        Guid grantId,
        string voidedByExternalUserId,
        string? reason,
        DateTimeOffset voidedAtUtc,
        CancellationToken ct)
    {
        var affected = await _db.RewardGrants
            .Where(grant => grant.Id == grantId && grant.Status == GrantStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(grant => grant.Status, GrantStatus.Voided)
                    .SetProperty(grant => grant.VoidedByExternalUserId, voidedByExternalUserId)
                    .SetProperty(grant => grant.VoidReason, reason)
                    .SetProperty(grant => grant.VoidedAtUtc, voidedAtUtc),
                ct);

        return affected == 1;
    }
}
