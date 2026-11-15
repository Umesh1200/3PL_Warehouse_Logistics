using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> builder)
    {
        builder.ToTable("receipt_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("receipt_line_id")
            .ValueGeneratedNever();

        builder.Property(l => l.ReceivingSessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(l => l.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(l => l.PurchaseOrderLineId)
            .HasColumnName("purchase_order_line_id");

        builder.Property(l => l.AsnLineItemId)
            .HasColumnName("asn_line_item_id");

        builder.Property(l => l.Lpn)
            .HasColumnName("lpn")
            .HasMaxLength(60)
            .IsRequired();

        builder.HasIndex(l => new { l.ReceivingSessionId, l.Lpn });

        builder.Property(l => l.QuantityReceived)
            .HasColumnName("quantity_received")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.QuantityExpected)
            .HasColumnName("quantity_expected")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.LotNumber)
            .HasColumnName("lot_number")
            .HasMaxLength(50);

        builder.Property(l => l.SerialNumber)
            .HasColumnName("serial_number")
            .HasMaxLength(100);

        builder.Property(l => l.CatchWeightKg)
            .HasColumnName("catch_weight_kg")
            .HasPrecision(18, 4);

        builder.Property(l => l.ManufactureDate)
            .HasColumnName("manufacture_date");

        builder.Property(l => l.ExpirationDate)
            .HasColumnName("expiration_date");

        builder.Property(l => l.Condition)
            .HasColumnName("condition")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(l => l.StagingLocationId)
            .HasColumnName("staging_location_id");

        builder.Property(l => l.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(l => l.ScannedAtUtc)
            .HasColumnName("scanned_at_utc")
            .IsRequired();

        // Optimistic concurrency token
        builder.Property(l => l.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(l => l.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(l => l.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(l => l.DeletedBy)
            .HasColumnName("deleted_by");

        // Relationships
        builder.HasOne(l => l.ReceivingSession)
            .WithMany(s => s.Lines)
            .HasForeignKey(l => l.ReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Item)
            .WithMany()
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(l => l.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.AsnLineItem)
            .WithMany()
            .HasForeignKey(l => l.AsnLineItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.StagingLocation)
            .WithMany()
            .HasForeignKey(l => l.StagingLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(l => l.Discrepancies)
            .WithOne(d => d.ReceiptLine)
            .HasForeignKey(d => d.ReceiptLineId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

