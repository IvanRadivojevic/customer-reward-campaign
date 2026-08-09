namespace Campaign.Infrastructure.Persistence.Configurations;

using Campaign.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches");

        builder.HasKey(batch => batch.Id);

        builder.Property(batch => batch.FileName)
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(batch => batch.FileSha256)
            .HasColumnType("char(64)")
            .IsRequired();

        builder.Property(batch => batch.UploadedBy)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(batch => batch.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        // P-08: the same file in the same campaign cannot produce a second batch. This index is the
        // rule itself, which is why the import attempts the insert instead of asking first.
        builder.HasIndex(batch => new { batch.CampaignId, batch.FileSha256 })
            .IsUnique()
            .HasDatabaseName("UX_ImportBatches_Campaign_FileSha256");

        builder.HasOne<Campaign>()
            .WithMany()
            .HasForeignKey(batch => batch.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
