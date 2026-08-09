namespace Campaign.Infrastructure.Persistence.Configurations;

using Campaign.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");

        builder.HasKey(campaign => campaign.Id);

        builder.Property(campaign => campaign.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(campaign => campaign.DiscountPercent)
            .HasPrecision(5, 2);

        // Statuses are stored as text, not as numbers: the filtered index for P-03 and the check
        // constraints on purchase rows are written against the value, and a report read straight
        // from the database should be legible without a lookup table.
        builder.Property(campaign => campaign.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
    }
}
