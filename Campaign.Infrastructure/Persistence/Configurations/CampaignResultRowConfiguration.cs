namespace Campaign.Infrastructure.Persistence.Configurations;

using Campaign.Core.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// The results view has no key and no table: it is read only, and the counting behind it is the SQL
/// in the migration that creates it.
/// </summary>
internal sealed class CampaignResultRowConfiguration : IEntityTypeConfiguration<CampaignResultRow>
{
    public const string ViewName = "vw_CampaignResults";

    public void Configure(EntityTypeBuilder<CampaignResultRow> builder)
    {
        builder.HasNoKey();
        builder.ToView(ViewName);
    }
}
