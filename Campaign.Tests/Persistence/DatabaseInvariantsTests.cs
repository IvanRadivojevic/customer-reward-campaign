namespace Campaign.Tests.Persistence;

using Campaign.Core.Domain;
using Campaign.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// These tests write SQL by hand on purpose. The point is not that the use cases behave, but that a
/// row which breaks a rule cannot enter the database even when nothing in this solution puts it
/// there - a second application, an import job or somebody with SSMS open.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public class DatabaseInvariantsTests
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int CheckConstraintViolation = 547;

    private static readonly DateOnly Today = new(2026, 8, 9);
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

    private readonly SqlServerFixture _fixture;

    public DatabaseInvariantsTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_migration_creates_the_indexes_and_check_constraints_the_rules_rely_on()
    {
        await using var db = _fixture.CreateContext();

        var filter = await ReadListAsync<string>(
            db,
            "SELECT filter_definition AS Value FROM sys.indexes WHERE name = 'UX_RewardGrants_Campaign_Customer_Active'");

        // The filter is what makes a voided grant stop occupying its customer.
        Assert.Contains("Active", Assert.Single(filter));

        var indexes = await ReadListAsync<string>(
            db,
            "SELECT name AS Value FROM sys.indexes WHERE name IS NOT NULL");

        Assert.Contains("UX_RewardGrants_Campaign_Customer_Active", indexes);
        Assert.Contains("UX_RewardGrants_Agent_IdempotencyKey", indexes);
        Assert.Contains("UX_ImportBatches_Campaign_FileSha256", indexes);
        Assert.Contains("IX_RewardGrants_Agent_Campaign_BusinessDate_Status", indexes);

        var checks = await ReadListAsync<string>(
            db,
            "SELECT name AS Value FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('PurchaseResults')");

        Assert.Contains("CK_PurchaseResults_RequiredFieldsUnlessInvalid", checks);
        Assert.Contains("CK_PurchaseResults_AmountAndCurrencyTogether", checks);
    }

    [Fact]
    public async Task P03_the_database_rejects_a_second_active_grant_for_the_same_customer()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedCampaignAndAgentAsync(db);
        var customerExternalId = NewExternalId();

        await InsertGrantAsync(db, world, customerExternalId, GrantStatus.Active);

        var error = await Assert.ThrowsAsync<SqlException>(
            () => InsertGrantAsync(db, world, customerExternalId, GrantStatus.Active));

        Assert.Contains(error.Number, new[] { UniqueIndexViolation, UniqueConstraintViolation });
    }

    [Fact]
    public async Task P03_a_voided_grant_leaves_the_customer_free_for_a_new_grant()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedCampaignAndAgentAsync(db);
        var customerExternalId = NewExternalId();

        await InsertGrantAsync(db, world, customerExternalId, GrantStatus.Voided);
        await InsertGrantAsync(db, world, customerExternalId, GrantStatus.Voided);

        // Two voided grants and one active one for the same customer: the filtered index only ever
        // counts the active row, which is how voiding frees the customer without deleting history.
        await InsertGrantAsync(db, world, customerExternalId, GrantStatus.Active);

        var active = await db.RewardGrants.CountAsync(grant =>
            grant.CampaignId == world.CampaignId
            && grant.CustomerExternalId == customerExternalId
            && grant.Status == GrantStatus.Active);

        Assert.Equal(1, active);
    }

    [Fact]
    public async Task P06_the_database_rejects_one_idempotency_key_used_twice_by_the_same_agent()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedCampaignAndAgentAsync(db);
        var key = Guid.NewGuid().ToString();

        await InsertGrantAsync(db, world, NewExternalId(), GrantStatus.Active, key);

        var error = await Assert.ThrowsAsync<SqlException>(
            () => InsertGrantAsync(db, world, NewExternalId(), GrantStatus.Active, key));

        Assert.Contains(error.Number, new[] { UniqueIndexViolation, UniqueConstraintViolation });
    }

    [Fact]
    public async Task P08_the_database_rejects_the_same_file_twice_in_one_campaign()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedCampaignAndAgentAsync(db);
        var sha256 = NewSha256();

        await InsertBatchAsync(db, world.CampaignId, sha256);

        var error = await Assert.ThrowsAsync<SqlException>(
            () => InsertBatchAsync(db, world.CampaignId, sha256));

        Assert.Contains(error.Number, new[] { UniqueIndexViolation, UniqueConstraintViolation });
    }

    [Fact]
    public async Task An_invalid_row_is_accepted_without_a_customer_id_and_without_a_purchase_date()
    {
        await using var db = _fixture.CreateContext();
        var batchId = await SeedBatchAsync(db);

        await InsertPurchaseRowAsync(
            db,
            batchId,
            matchStatus: "Invalid",
            customerExternalId: null,
            purchaseDate: null,
            amount: null,
            currency: null,
            error: "PurchaseDate is not a date.");

        Assert.Equal(1, await db.PurchaseResults.CountAsync(row => row.BatchId == batchId));
    }

    [Fact]
    public async Task The_same_row_marked_as_matched_instead_of_invalid_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var batchId = await SeedBatchAsync(db);

        var error = await Assert.ThrowsAsync<SqlException>(() => InsertPurchaseRowAsync(
            db,
            batchId,
            matchStatus: "Matched",
            customerExternalId: null,
            purchaseDate: null,
            amount: null,
            currency: null,
            error: null));

        Assert.Equal(CheckConstraintViolation, error.Number);
    }

    [Fact]
    public async Task A_row_with_an_amount_and_no_currency_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var batchId = await SeedBatchAsync(db);

        var error = await Assert.ThrowsAsync<SqlException>(() => InsertPurchaseRowAsync(
            db,
            batchId,
            matchStatus: "Unmatched",
            customerExternalId: "1",
            purchaseDate: new DateOnly(2026, 9, 14),
            amount: 149.90m,
            currency: null,
            error: null));

        Assert.Equal(CheckConstraintViolation, error.Number);
    }

    [Fact]
    public async Task A_row_with_a_currency_and_no_amount_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var batchId = await SeedBatchAsync(db);

        var error = await Assert.ThrowsAsync<SqlException>(() => InsertPurchaseRowAsync(
            db,
            batchId,
            matchStatus: "Unmatched",
            customerExternalId: "1",
            purchaseDate: new DateOnly(2026, 9, 14),
            amount: null,
            currency: "EUR",
            error: null));

        Assert.Equal(CheckConstraintViolation, error.Number);
    }

    private static string NewExternalId() => Guid.NewGuid().ToString("N")[..12];

    private static string NewSha256() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static async Task<(Guid CampaignId, Guid AgentId)> SeedCampaignAndAgentAsync(AppDbContext db)
    {
        var campaign = Campaign.Create(
            Guid.NewGuid(),
            "Invariant test campaign",
            Today.AddDays(-3),
            Today.AddDays(3),
            discountPercent: 10m,
            CampaignStatus.Active);

        var agent = Agent.Create(Guid.NewGuid(), $"agent-{Guid.NewGuid():N}", "Invariant test agent");

        db.Campaigns.Add(campaign);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        return (campaign.Id, agent.Id);
    }

    private static async Task<Guid> SeedBatchAsync(AppDbContext db)
    {
        var world = await SeedCampaignAndAgentAsync(db);
        return await InsertBatchAsync(db, world.CampaignId, NewSha256());
    }

    private static async Task<Guid> InsertBatchAsync(AppDbContext db, Guid campaignId, string sha256)
    {
        var id = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ImportBatches
                (Id, CampaignId, FileName, FileSha256, UploadedAtUtc, UploadedBy,
                 RowsTotal, RowsMatched, RowsUnmatched, RowsInvalid, Status)
            VALUES
                ({id}, {campaignId}, {"purchases.csv"}, {sha256}, {Now}, {"integration"},
                 {0}, {0}, {0}, {0}, {"Completed"})
            """);

        return id;
    }

    private static Task InsertGrantAsync(
        AppDbContext db,
        (Guid CampaignId, Guid AgentId) world,
        string customerExternalId,
        GrantStatus status,
        string? idempotencyKey = null) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO RewardGrants
                (Id, CampaignId, AgentId, CustomerExternalId, CustomerNameAtGrant, BusinessDate,
                 GrantedAtUtc, DiscountPercent, Status, IdempotencyKey)
            VALUES
                ({Guid.NewGuid()}, {world.CampaignId}, {world.AgentId}, {customerExternalId}, {"Customer"},
                 {Today}, {Now}, {10.00m}, {status.ToString()}, {idempotencyKey ?? Guid.NewGuid().ToString()})
            """);

    private static Task InsertPurchaseRowAsync(
        AppDbContext db,
        Guid batchId,
        string matchStatus,
        string? customerExternalId,
        DateOnly? purchaseDate,
        decimal? amount,
        string? currency,
        string? error) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PurchaseResults
                (Id, BatchId, RowNumber, RawLine, CustomerExternalId, PurchaseDate, Amount, Currency,
                 MatchedGrantId, MatchStatus, Error)
            VALUES
                ({Guid.NewGuid()}, {batchId}, {1}, {"1,2026-09-14,149.90,EUR"}, {customerExternalId},
                 {purchaseDate}, {amount}, {currency}, NULL, {matchStatus}, {error})
            """);

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(AppDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<T>(sql).ToListAsync();
}
