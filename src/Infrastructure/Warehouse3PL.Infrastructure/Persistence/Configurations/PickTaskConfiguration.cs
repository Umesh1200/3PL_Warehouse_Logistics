using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PickTaskConfiguration : IEntityTypeConfiguration<PickTask>
{
    public void Configure(EntityTypeBuilder<PickTask> builder)
    {
        builder.ToTable("pick_tasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("pick_task_id")
            .ValueGeneratedNever();

        builder.Property(t => t.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(t => t.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(t => t.PickWaveId)
            .HasColumnName("pick_wave_id")
            .IsRequired();

        builder.Property(t => t.OutboundOrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(t => t.OutboundOrderLineId)
            .HasColumnName("line_id")
            .IsRequired();

        builder.Property(t => t.InventoryAllocationId)
            .HasColumnName("allocation_id")
            .IsRequired();

        builder.Property(t => t.StockOnHandId)
            .HasColumnName("stock_id")
            .IsRequired();

        builder.Property(t => t.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(t => t.SourceLocationId)
            .HasColumnName("source_location_id")
            .IsRequired();

        builder.Property(t => t.DestinationLocationId)
            .HasColumnName("destination_location_id");

        builder.Property(t => t.LotId)
            .HasColumnName("lot_id");

        builder.Property(t => t.ExpectedLpnCode)
            .HasColumnName("expected_lpn_code")
            .HasMaxLength(100);

        builder.Property(t => t.TargetHandlingUnitId)
            .HasColumnName("target_handling_unit_id");

        builder.Property(t => t.TargetToteBarcode)
            .HasColumnName("target_tote_barcode")
            .HasMaxLength(100);

        builder.Property(t => t.QuantityToPick)
            .HasColumnName("quantity_to_pick")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(t => t.QuantityPicked)
            .HasColumnName("quantity_picked")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Ignore(t => t.ShortPickQuantity);

        builder.Property(t => t.PickSequence)
            .HasColumnName("pick_sequence")
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(t => t.ShortPickReason)
            .HasColumnName("short_pick_reason")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(t => t.ShortPickNotes)
            .HasColumnName("short_pick_notes")
            .HasMaxLength(500);

        builder.Property(t => t.AssignedUserId)
            .HasColumnName("assigned_user_id");

        builder.Property(t => t.AssignedAtUtc)
            .HasColumnName("assigned_at_utc");

        builder.Property(t => t.StartedAtUtc)
            .HasColumnName("started_at_utc");

        builder.Property(t => t.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(t => t.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1u);

        // Soft delete
        builder.Property(t => t.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");
        builder.Property(t => t.DeletedBy).HasColumnName("deleted_by");

        // Auditing
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.CreatedBy).HasColumnName("created_by");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(t => new { t.ClientId, t.FacilityId });
        builder.HasIndex(t => t.PickWaveId);
        builder.HasIndex(t => t.OutboundOrderId);
        builder.HasIndex(t => t.InventoryAllocationId);
        builder.HasIndex(t => t.SourceLocationId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.AssignedUserId);
        builder.HasIndex(t => t.PickSequence);

        // Relationships
        builder.HasOne(t => t.Client)
            .WithMany()
            .HasForeignKey(t => t.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Facility)
            .WithMany()
            .HasForeignKey(t => t.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.PickWave)
            .WithMany(w => w.Tasks)
            .HasForeignKey(t => t.PickWaveId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.OutboundOrder)
            .WithMany()
            .HasForeignKey(t => t.OutboundOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.OutboundOrderLine)
            .WithMany()
            .HasForeignKey(t => t.OutboundOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.InventoryAllocation)
            .WithMany()
            .HasForeignKey(t => t.InventoryAllocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.StockOnHand)
            .WithMany()
            .HasForeignKey(t => t.StockOnHandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Item)
            .WithMany()
            .HasForeignKey(t => t.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.SourceLocation)
            .WithMany()
            .HasForeignKey(t => t.SourceLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.DestinationLocation)
            .WithMany()
            .HasForeignKey(t => t.DestinationLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Lot)
            .WithMany()
            .HasForeignKey(t => t.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.TargetHandlingUnit)
            .WithMany()
            .HasForeignKey(t => t.TargetHandlingUnitId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.AssignedUser)
            .WithMany()
            .HasForeignKey(t => t.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

