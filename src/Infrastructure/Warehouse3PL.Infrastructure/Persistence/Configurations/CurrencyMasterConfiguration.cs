using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CurrencyMasterConfiguration : IEntityTypeConfiguration<CurrencyMaster>
{
    public void Configure(EntityTypeBuilder<CurrencyMaster> builder)
    {
        builder.ToTable("currencies", t =>
        {
            t.HasCheckConstraint(
                "ck_currencies_minor_units",
                "minor_units BETWEEN 0 AND 4");
        });

        builder.HasKey(c => c.CurrencyCode);
        builder.Property(c => c.CurrencyCode)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(c => c.NumericCode)
            .HasColumnName("numeric_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.HasIndex(c => c.NumericCode)
            .IsUnique();

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(c => c.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(c => c.MinorUnits)
            .HasColumnName("minor_units")
            .IsRequired();

        builder.Property(c => c.SymbolBeforeAmount)
            .HasColumnName("symbol_before_amount")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.SeedVersion)
            .HasColumnName("seed_version")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
    }
}
