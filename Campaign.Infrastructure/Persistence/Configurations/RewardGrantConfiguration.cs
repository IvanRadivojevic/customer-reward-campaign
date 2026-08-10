namespace Campaign.Infrastructure.Persistence.Configurations;

using Campaign.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class RewardGrantConfiguration : IEntityTypeConfiguration<RewardGrant>
{
    /// <summary>P-03. Named here because the unit of work reads it back out of a SQL error message.</summary>
    public const string ActiveCustomerIndexName = "UX_RewardGrants_Campaign_Customer_Active";

    /// <summary>P-06, for the same reason.</summary>
    public const string IdempotencyKeyIndexName = "UX_RewardGrants_Agent_IdempotencyKey";

    public void Configure(EntityTypeBuilder<RewardGrant> builder)
    {
        builder.ToTable("RewardGrants");

        builder.HasKey(grant => grant.Id);

        builder.Property(grant => grant.CustomerExternalId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(grant => grant.CustomerNameAtGrant)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(grant => grant.IdempotencyKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(grant => grant.DiscountPercent)
            .HasPrecision(5, 2);

        builder.Property(grant => grant.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(grant => grant.VoidedByExternalUserId)
            .HasMaxLength(128);

        builder.Property(grant => grant.VoidReason)
            .HasMaxLength(RewardGrant.MaxVoidReasonLength);

        // P-03: one active grant per customer per campaign, across all agents. The filter is what
        // makes a voided grant stop occupying the customer.
        builder.HasIndex(grant => new { grant.CampaignId, grant.CustomerExternalId })
            .IsUnique()
            .HasFilter("[Status] = 'Active'")
            .HasDatabaseName(ActiveCustomerIndexName);

        // P-06: the same agent cannot use one idempotency key for two grants.
        builder.HasIndex(grant => new { grant.AgentId, grant.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName(IdempotencyKeyIndexName);

        // P-02: the exact shape of the COUNT the daily limit performs on every grant.
        builder.HasIndex(grant => new { grant.AgentId, grant.CampaignId, grant.BusinessDate, grant.Status })
            .HasDatabaseName("IX_RewardGrants_Agent_Campaign_BusinessDate_Status");

        // No cascade anywhere: business records are never deleted, so a delete that reaches this
        // table should fail loudly rather than take the history with it.
        builder.HasOne<Campaign>()
            .WithMany()
            .HasForeignKey(grant => grant.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(grant => grant.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
