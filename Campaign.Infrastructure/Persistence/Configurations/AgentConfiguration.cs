namespace Campaign.Infrastructure.Persistence.Configurations;

using Campaign.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");

        builder.HasKey(agent => agent.Id);

        builder.Property(agent => agent.ExternalUserId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(agent => agent.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        // The subject claim of a token has to identify exactly one agent, otherwise the lookup that
        // turns a token into a record owner has no single answer.
        builder.HasIndex(agent => agent.ExternalUserId)
            .IsUnique()
            .HasDatabaseName("UX_Agents_ExternalUserId");
    }
}
