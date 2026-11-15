using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InventoryAllocationConfiguration : IEntityTypeConfiguration<InventoryAllocation>
{
    public void Configure(EntityTypeBuilder<InventoryAllocation> builder)
    {
        builder.ToTable("inventory_allocations");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("allocation_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.OutboundOrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(a => a.OutboundOrderLineId)
            .HasColumnName("line_id")
            .IsRequired();

        builder.Property(a => a.StockOnHandId)
            .HasColumnName("stock_id")
            .IsRequired();

        builder.Property(a => a.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(a => a.LocationId)
            .HasColumnName("location_id")
            .IsRequired();

        builder.Property(a => a.LotId)
            .HasColumnName("lot_id");

        builder.Property(a => a.LpnCode)
            .HasColumnName("lpn_code")
            .HasMaxLength(100);

        builder.Property(a => a.QuantityRequested)
            .HasColumnName("quantity_requested")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(a => a.QuantityAllocated)
            .HasColumnName("quantity_allocated")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(a => a.QuantityReleased)
            .HasColumnName("quantity_released")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(a => a.QuantityPicked)
            .HasColumnName("quantity_picked")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(a => a.PickedStockOnHandId)
            .HasColumnName("picked_stock_id");

        builder.Property(a => a.TargetHandlingUnitId)
            .HasColumnName("target_handling_unit_id");

        builder.Property(a => a.TargetLpnCode)
            .HasColumnName("target_lpn_code")
            .HasMaxLength(100);

        builder.Ignore(a => a.ActiveAllocatedQuantity);
        builder.Ignore(a => a.RemainingToPickQuantity);

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.StrategyUsed)
            .HasColumnName("strategy_used")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.AllocatedAtUtc)
            .HasColumnName("allocated_at_utc")
            .IsRequired();

        builder.Property(a => a.AllocatedByUserId)
            .HasColumnName("allocated_by_user_id");

        builder.Property(a => a.ReleasedAtUtc)
            .HasColumnName("released_at_utc");

        builder.Property(a => a.ReleasedByUserId)
            .HasColumnName("released_by_user_id");

        builder.Property(a => a.ReleaseReason)
            .HasColumnName("release_reason")
            .HasMaxLength(500);

        builder.Property(a => a.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1u);

        // Soft delete
        builder.Property(a => a.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(a => a.DeletedAt).HasColumnName("deleted_at");
        builder.Property(a => a.DeletedBy).HasColumnName("deleted_by");

        // Auditing
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(a => new { a.ClientId, a.FacilityId });
        builder.HasIndex(a => a.OutboundOrderId);
        builder.HasIndex(a => a.OutboundOrderLineId);
        builder.HasIndex(a => a.StockOnHandId);
        builder.HasIndex(a => a.ItemId);
        builder.HasIndex(a => a.LocationId);
        builder.HasIndex(a => a.LotId);
        builder.HasIndex(a => a.Status);

        // Relationships
        builder.HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Facility)
            .WithMany()
            .HasForeignKey(a => a.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.OutboundOrder)
            .WithMany(o => o.Allocations)
            .HasForeignKey(a => a.OutboundOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.OutboundOrderLine)
            .WithMany(l => l.Allocations)
            .HasForeignKey(a => a.OutboundOrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.StockOnHand)
            .WithMany()
            .HasForeignKey(a => a.StockOnHandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Item)
            .WithMany()
            .HasForeignKey(a => a.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Location)
            .WithMany()
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Lot)
            .WithMany()
            .HasForeignKey(a => a.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.PickedStockOnHand)
            .WithMany()
            .HasForeignKey(a => a.PickedStockOnHandId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.TargetHandlingUnit)
            .WithMany()
            .HasForeignKey(a => a.TargetHandlingUnitId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

