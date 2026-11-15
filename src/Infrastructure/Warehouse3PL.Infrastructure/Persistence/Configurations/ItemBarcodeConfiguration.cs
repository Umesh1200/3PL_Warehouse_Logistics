using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Catalog;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ItemBarcodeConfiguration : IEntityTypeConfiguration<ItemBarcode>
{
    public void Configure(EntityTypeBuilder<ItemBarcode> builder)
    {
        builder.ToTable("item_barcodes");

        builder.HasKey(ib => ib.Id);
        builder.Property(ib => ib.Id)
            .HasColumnName("barcode_id")
            .ValueGeneratedNever();

        builder.Property(ib => ib.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(ib => ib.BarcodeValue)
            .HasColumnName("barcode_value")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(ib => ib.Symbology)
            .HasColumnName("barcode_symbology")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(ib => ib.IsPrimary)
            .HasColumnName("is_primary")
            .HasDefaultValue(false);

        // Indexes
        builder.HasIndex(ib => ib.ItemId);
        builder.HasIndex(ib => ib.BarcodeValue);

        // Relationship
        builder.HasOne(ib => ib.Item)
            .WithMany(i => i.Barcodes)
            .HasForeignKey(ib => ib.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

