using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ConfigurationScopeEpochConfiguration : IEntityTypeConfiguration<ConfigurationScopeEpoch>
{
    public void Configure(EntityTypeBuilder<ConfigurationScopeEpoch> builder)
    {
        builder.ToTable("configuration_scope_epochs", t =>
        {
            t.HasCheckConstraint("ck_configuration_scope_epochs_positive", "current_epoch >= 1");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("epoch_id").ValueGeneratedNever();

        builder.Property(e => e.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.ScopeKey)
            .HasColumnName("scope_key")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(e => e.CurrentEpoch)
            .HasColumnName("current_epoch")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex(e => new { e.ScopeType, e.ScopeKey }).IsUnique();
    }
}
