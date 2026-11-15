using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReturnReceivingLineConfiguration : IEntityTypeConfiguration<ReturnReceivingLine>
{
    public void Configure(EntityTypeBuilder<ReturnReceivingLine> builder)
    {
        builder.ToTable("return_receiving_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("line_id")
            .ValueGeneratedNever();

        builder.Property(l => l.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(l => l.ReturnReceivingSessionId)
            .HasColumnName("return_receiving_session_id")
            .IsRequired();

        builder.Property(l => l.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(l => l.OriginalOrderLineId)
            .HasColumnName("original_order_line_id");

        builder.Property(l => l.Sku)
            .HasColumnName("sku")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(l => l.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(l => l.ExpectedQuantity)
            .HasColumnName("expected_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.ReceivedQuantity)
            .HasColumnName("received_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.AcceptedQuantity)
            .HasColumnName("accepted_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.DamagedQuantity)
            .HasColumnName("damaged_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.QuarantinedQuantity)
            .HasColumnName("quarantined_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.Disposition)
            .HasColumnName("disposition")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(l => l.TargetLocationId)
            .HasColumnName("target_location_id");

        builder.Property(l => l.InspectionNotes)
            .HasColumnName("inspection_notes")
            .HasMaxLength(500);

        builder.Property(l => l.InspectedAtUtc)
            .HasColumnName("inspected_at_utc");

        builder.Property(l => l.IsInventoryPosted)
            .HasColumnName("is_inventory_posted")
            .IsRequired();

        builder.Property(l => l.InventoryTransactionId)
            .HasColumnName("inventory_transaction_id");

        // Indexes
        builder.HasIndex(l => new { l.ReturnReceivingSessionId, l.ItemId })
            .HasDatabaseName("ix_return_receiving_lines_session_item");

        builder.HasIndex(l => new { l.ClientId, l.Sku })
            .HasDatabaseName("ix_return_receiving_lines_client_sku");

        // Relationships
        builder.HasOne(l => l.ReturnReceivingSession)
            .WithMany(s => s.Lines)
            .HasForeignKey(l => l.ReturnReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Item)
            .WithMany()
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.TargetLocation)
            .WithMany()
            .HasForeignKey(l => l.TargetLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

