namespace Campaign.Infrastructure.Persistence;

using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<RewardGrant> RewardGrants => Set<RewardGrant>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<PurchaseResult> PurchaseResults => Set<PurchaseResult>();

    /// <summary>The campaign results view. Read only: the counting happens in SQL, not here.</summary>
    public DbSet<CampaignResultRow> CampaignResults => Set<CampaignResultRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Nothing in this API loads an entity in order to edit it - a void is a conditional UPDATE
        // and a correction is a new record - so change tracking on queries would only cost memory
        // and make a retried transaction harder to reason about.
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
