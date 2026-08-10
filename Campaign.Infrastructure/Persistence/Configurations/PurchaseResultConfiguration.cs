namespace Campaign.Infrastructure.Persistence.Configurations;

using Campaign.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class PurchaseResultConfiguration : IEntityTypeConfiguration<PurchaseResult>
{
    public void Configure(EntityTypeBuilder<PurchaseResult> builder)
    {
        builder.ToTable(
            "PurchaseResults",
            table =>
            {
                // A row that is not invalid has to carry the two mandatory fields. An invalid row
                // carries none of them, which is why this constraint and the next one can never
                // collide with the rows the import writes for broken lines.
                table.HasCheckConstraint(
                    "CK_PurchaseResults_RequiredFieldsUnlessInvalid",
                    "[MatchStatus] = 'Invalid' OR ([CustomerExternalId] IS NOT NULL AND [PurchaseDate] IS NOT NULL)");

                // An amount without a currency is ambiguous data, so the two travel as a pair.
                table.HasCheckConstraint(
                    "CK_PurchaseResults_AmountAndCurrencyTogether",
                    "([Amount] IS NULL AND [Currency] IS NULL) OR ([Amount] IS NOT NULL AND [Currency] IS NOT NULL)");
            });

        builder.HasKey(row => row.Id);

        builder.Property(row => row.RawLine)
            .IsRequired();

        builder.Property(row => row.CustomerExternalId)
            .HasMaxLength(PurchaseResult.MaxCustomerExternalIdLength);

        builder.Property(row => row.Amount)
            .HasPrecision(18, 2);

        builder.Property(row => row.Currency)
            .HasColumnType($"char({PurchaseResult.CurrencyCodeLength})");

        builder.Property(row => row.MatchStatus)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(row => row.Error)
            .HasMaxLength(PurchaseResult.MaxErrorLength);

        builder.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(row => row.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RewardGrant>()
            .WithMany()
            .HasForeignKey(row => row.MatchedGrantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
