using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class UomConversionConfiguration : IEntityTypeConfiguration<UomConversion>
{
    public void Configure(EntityTypeBuilder<UomConversion> builder)
    {
        builder.ToTable("uom_conversions", t =>
        {
            t.HasCheckConstraint(
                "ck_uom_conversions_distinct_uoms",
                "from_uom_code <> to_uom_code");
            t.HasCheckConstraint(
                "ck_uom_conversions_positive_factors",
                "conversion_factor > 0 AND inverse_factor > 0");
        });

        builder.HasKey(c => new { c.FromUomCode, c.ToUomCode });

        builder.Property(c => c.FromUomCode)
            .HasColumnName("from_uom_code")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.ToUomCode)
            .HasColumnName("to_uom_code")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.Dimension)
            .HasColumnName("dimension")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(c => c.ConversionFactor)
            .HasColumnName("conversion_factor")
            .HasPrecision(18, 10)
            .IsRequired();

        builder.Property(c => c.InverseFactor)
            .HasColumnName("inverse_factor")
            .HasPrecision(18, 10)
            .IsRequired();

        builder.Property(c => c.IsExact)
            .HasColumnName("is_exact")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(c => c.FromUom)
            .WithMany(d => d.ConversionsFrom)
            .HasForeignKey(c => c.FromUomCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ToUom)
            .WithMany(d => d.ConversionsTo)
            .HasForeignKey(c => c.ToUomCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
