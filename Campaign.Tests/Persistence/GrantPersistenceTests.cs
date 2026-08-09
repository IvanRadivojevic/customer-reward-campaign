namespace Campaign.Tests.Persistence;

using Campaign.Core.Domain;
using Campaign.Core.UseCases;
using Campaign.Infrastructure.Persistence;
using Campaign.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The same rules the unit tests cover, run once through the real repositories and the real
/// transaction, so the wiring described in SPEC section 5 is exercised and not only described.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public class GrantPersistenceTests
{
    private const string TimeZoneId = "Europe/Belgrade";

    private static readonly DateTimeOffset Now = new(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 9);

    private readonly SqlServerFixture _fixture;

    public GrantPersistenceTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateGrant_stores_the_grant_through_the_serializable_transaction()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedAsync(db);

        var result = await BuildCreateGrant(db).ExecuteAsync(
            new CreateGrantCommand(world.CampaignId, "1", world.AgentExternalUserId, Guid.NewGuid().ToString()),
            CancellationToken.None);

        await using var reader = _fixture.CreateContext();
        var stored = await reader.RewardGrants.FirstOrDefaultAsync(grant => grant.Id == result.Grant.Id);

        Assert.NotNull(stored);
        Assert.Equal(GrantStatus.Active, stored.Status);
        Assert.Equal(Today, stored.BusinessDate);
        Assert.Equal("Customer 1", stored.CustomerNameAtGrant);
        Assert.Equal(10m, stored.DiscountPercent);
    }

    [Fact]
    public async Task P02_the_daily_limit_is_enforced_over_the_real_database()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedAsync(db, dailyLimitPerAgent: 2);
        var useCase = BuildCreateGrant(db);

        await useCase.ExecuteAsync(
            new CreateGrantCommand(world.CampaignId, "1", world.AgentExternalUserId, Guid.NewGuid().ToString()),
            CancellationToken.None);
        await useCase.ExecuteAsync(
            new CreateGrantCommand(world.CampaignId, "2", world.AgentExternalUserId, Guid.NewGuid().ToString()),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(() => useCase.ExecuteAsync(
            new CreateGrantCommand(world.CampaignId, "3", world.AgentExternalUserId, Guid.NewGuid().ToString()),
            CancellationToken.None));

        Assert.Equal(DomainErrorCodes.DailyLimitReached, error.Code);
        Assert.Equal(2, error.Details["used"]);

        // The refused attempt left nothing behind: the transaction was rolled back.
        await using var reader = _fixture.CreateContext();
        var stored = await reader.RewardGrants.CountAsync(grant => grant.CampaignId == world.CampaignId);
        Assert.Equal(2, stored);
    }

    [Fact]
    public async Task P05_the_conditional_void_changes_one_row_and_then_reports_no_change()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedAsync(db);
        var repository = new EfGrantRepository(db);

        var granted = await BuildCreateGrant(db).ExecuteAsync(
            new CreateGrantCommand(world.CampaignId, "1", world.AgentExternalUserId, Guid.NewGuid().ToString()),
            CancellationToken.None);

        var first = await repository.TryVoidAsync(granted.Grant.Id, "agent-x", "Wrong customer", Now, CancellationToken.None);
        var second = await repository.TryVoidAsync(granted.Grant.Id, "agent-x", "Again", Now, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);

        await using var reader = _fixture.CreateContext();
        var stored = await reader.RewardGrants.SingleAsync(grant => grant.Id == granted.Grant.Id);

        Assert.Equal(GrantStatus.Voided, stored.Status);
        Assert.Equal("Wrong customer", stored.VoidReason);
        Assert.Equal("agent-x", stored.VoidedByExternalUserId);
    }

    private CreateGrant BuildCreateGrant(AppDbContext db)
    {
        var directory = new FakeCustomerDirectory();
        for (var i = 1; i <= 5; i++)
        {
            directory.With(i.ToString(), $"Customer {i}");
        }

        return new CreateGrant(
            new EfGrantRepository(db),
            directory,
            new EfUnitOfWork(db),
            new BusinessDateProvider(new FixedTimeProvider(Now), TimeZoneId));
    }

    private static async Task<(Guid CampaignId, string AgentExternalUserId)> SeedAsync(
        AppDbContext db,
        int dailyLimitPerAgent = 5)
    {
        var campaign = Campaign.Create(
            Guid.NewGuid(),
            "Persistence test campaign",
            Today.AddDays(-3),
            Today.AddDays(3),
            discountPercent: 10m,
            CampaignStatus.Active,
            dailyLimitPerAgent);

        var agent = Agent.Create(Guid.NewGuid(), $"agent-{Guid.NewGuid():N}", "Persistence test agent");

        db.Campaigns.Add(campaign);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (campaign.Id, agent.ExternalUserId);
    }
}
