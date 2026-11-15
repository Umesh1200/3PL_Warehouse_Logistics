using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Catalog;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PackagingUnitConfiguration : IEntityTypeConfiguration<PackagingUnit>
{
    public void Configure(EntityTypeBuilder<PackagingUnit> builder)
    {
        builder.ToTable("packaging_units");

        builder.HasKey(pu => pu.Id);
        builder.Property(pu => pu.Id)
            .HasColumnName("unit_id")
            .ValueGeneratedNever();

        builder.Property(pu => pu.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(pu => pu.UnitLevel)
            .HasColumnName("unit_level")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(pu => pu.UnitName)
            .HasColumnName("unit_name")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(pu => pu.ConversionQty)
            .HasColumnName("conversion_qty")
            .HasPrecision(12, 4)
            .IsRequired();

        builder.Property(pu => pu.WeightKg)
            .HasColumnName("weight_kg")
            .HasPrecision(10, 3);

        builder.Property(pu => pu.LengthCm)
            .HasColumnName("length_cm")
            .HasPrecision(10, 2);

        builder.Property(pu => pu.WidthCm)
            .HasColumnName("width_cm")
            .HasPrecision(10, 2);

        builder.Property(pu => pu.HeightCm)
            .HasColumnName("height_cm")
            .HasPrecision(10, 2);

        builder.Property(pu => pu.VolumeCm3)
            .HasColumnName("volume_cm3")
            .HasPrecision(14, 2);

        builder.Property(pu => pu.Barcode)
            .HasColumnName("barcode")
            .HasMaxLength(100);

        // Indexes
        builder.HasIndex(pu => pu.ItemId);
        builder.HasIndex(pu => pu.Barcode);

        // Relationship
        builder.HasOne(pu => pu.Item)
            .WithMany(i => i.PackagingUnits)
            .HasForeignKey(pu => pu.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

