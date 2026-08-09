namespace Campaign.Infrastructure.Persistence;

using Campaign.Core.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Applies the migrations on start and writes the seed when the database is still empty. The seed
/// is written here rather than through migration data, because the campaign window is relative to
/// today and a migration is a fixed script.
/// </summary>
public sealed class DatabaseInitializer
{
    private static readonly Guid SeedCampaignId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstAgentId = new("22222222-2222-2222-2222-222222222221");
    private static readonly Guid SecondAgentId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdAgentId = new("22222222-2222-2222-2222-222222222223");

    private readonly AppDbContext _db;
    private readonly BusinessDateProvider _businessDates;

    public DatabaseInitializer(AppDbContext db, BusinessDateProvider businessDates)
    {
        _db = db;
        _businessDates = businessDates;
    }

    public async Task InitialiseAsync(CancellationToken ct)
    {
        await _db.Database.MigrateAsync(ct);

        if (await _db.Campaigns.AnyAsync(ct) || await _db.Agents.AnyAsync(ct))
        {
            return;
        }

        // Three days back and three days forward, so the campaign is open today whenever the demo
        // is run, and both edges of the window are reachable without editing data.
        var today = _businessDates.Today();

        _db.Campaigns.Add(Campaign.Create(
            SeedCampaignId,
            "Loyal customers, weekly campaign",
            today.AddDays(-3),
            today.AddDays(3),
            discountPercent: 10m,
            CampaignStatus.Active));

        _db.Agents.AddRange(
            Agent.Create(FirstAgentId, "agent-1", "Marko Markovic"),
            Agent.Create(SecondAgentId, "agent-2", "Jelena Jelic"),
            Agent.Create(ThirdAgentId, "agent-3", "Petar Petrovic"));

        await _db.SaveChangesAsync(ct);
    }
}
