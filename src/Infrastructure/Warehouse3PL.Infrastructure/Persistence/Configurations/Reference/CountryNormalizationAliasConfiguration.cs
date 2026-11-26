using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations.Reference;

public sealed class CountryNormalizationAliasConfiguration : IEntityTypeConfiguration<CountryNormalizationAlias>
{
    public void Configure(EntityTypeBuilder<CountryNormalizationAlias> builder)
    {
        builder.ToTable("country_normalization_aliases");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("alias_id").ValueGeneratedNever();
        builder.Property(a => a.AliasKey).HasColumnName("alias_key").HasMaxLength(120).IsRequired();
        builder.Property(a => a.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        builder.Property(a => a.SourceStandard).HasColumnName("source_standard").HasMaxLength(60).IsRequired();
        builder.Property(a => a.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(a => a.AliasKey).IsUnique().HasDatabaseName("ux_country_normalization_aliases_key");
        builder.HasIndex(a => new { a.CountryCode, a.IsActive }).HasDatabaseName("ix_country_normalization_aliases_code_active");

        builder.HasOne(a => a.CountryProfile)
            .WithMany()
            .HasForeignKey(a => a.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
