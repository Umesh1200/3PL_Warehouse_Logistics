using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class AsnLineItemConfiguration : IEntityTypeConfiguration<AsnLineItem>
{
    public void Configure(EntityTypeBuilder<AsnLineItem> builder)
    {
        builder.ToTable("asn_line_items");

        builder.HasKey(al => al.Id);
        builder.Property(al => al.Id)
            .HasColumnName("asn_line_id")
            .ValueGeneratedNever();

        builder.Property(al => al.AsnId)
            .HasColumnName("asn_id")
            .IsRequired();

        builder.Property(al => al.PurchaseOrderLineId)
            .HasColumnName("po_line_id");

        builder.Property(al => al.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(al => al.ExpectedQty)
            .HasColumnName("expected_qty")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(al => al.ReceivedQty)
            .HasColumnName("received_qty")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(al => al.ExpectedLotNumber)
            .HasColumnName("expected_lot_number")
            .HasMaxLength(100);

        builder.Property(al => al.ManufactureDate)
            .HasColumnName("manufacture_date");

        builder.Property(al => al.ExpiryDate)
            .HasColumnName("expiry_date");

        builder.Property(al => al.PackagingUnitId)
            .HasColumnName("packaging_unit_id");

        builder.Property(al => al.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        // Relationships
        builder.HasOne(al => al.Item)
            .WithMany()
            .HasForeignKey(al => al.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(al => al.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(al => al.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(al => al.PackagingUnit)
            .WithMany()
            .HasForeignKey(al => al.PackagingUnitId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
