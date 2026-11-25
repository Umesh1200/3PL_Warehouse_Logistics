using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class UomDefinitionConfiguration : IEntityTypeConfiguration<UomDefinition>
{
    public void Configure(EntityTypeBuilder<UomDefinition> builder)
    {
        builder.ToTable("uom_definitions", t =>
        {
            t.HasCheckConstraint(
                "ck_uom_definitions_precision",
                "rounding_precision BETWEEN 0 AND 8");
        });

        builder.HasKey(d => d.UomCode);
        builder.Property(d => d.UomCode)
            .HasColumnName("uom_code")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(d => d.UneceRec20Code)
            .HasColumnName("unece_rec20_code")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(d => d.Name)
            .HasColumnName("name")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(d => d.Dimension)
            .HasColumnName("dimension")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(d => d.SystemOfMeasure)
            .HasColumnName("system_of_measure")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(d => d.IsCanonicalBase)
            .HasColumnName("is_canonical_base")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(d => d.RoundingPrecision)
            .HasColumnName("rounding_precision")
            .HasDefaultValue(4)
            .IsRequired();

        builder.Property(d => d.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(d => d.Dimension);
    }
}
