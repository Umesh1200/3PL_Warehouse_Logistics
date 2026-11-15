using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.CrossDock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CrossDockAllocationConfiguration : IEntityTypeConfiguration<CrossDockAllocation>
{
    public void Configure(EntityTypeBuilder<CrossDockAllocation> builder)
    {
        builder.ToTable("cross_dock_allocations");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("allocation_id").ValueGeneratedNever();

        builder.Property(a => a.CrossDockPlanLineId).HasColumnName("plan_line_id").IsRequired();
        builder.Property(a => a.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(a => a.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(a => a.ItemId).HasColumnName("item_id").IsRequired();
        builder.Property(a => a.LotId).HasColumnName("lot_id");
        builder.Property(a => a.InboundLpn).HasColumnName("inbound_lpn").HasMaxLength(100);
        builder.Property(a => a.OutboundOrderId).HasColumnName("outbound_order_id").IsRequired();
        builder.Property(a => a.OutboundOrderLineId).HasColumnName("outbound_order_line_id").IsRequired();
        builder.Property(a => a.AllocatedQuantity).HasColumnName("allocated_quantity").HasPrecision(14, 4).IsRequired();
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.AllocatedAtUtc).HasColumnName("allocated_at_utc").IsRequired();
        builder.Property(a => a.TransferredAtUtc).HasColumnName("transferred_at_utc");
        builder.Property(a => a.Version).HasColumnName("version").IsConcurrencyToken();

        // Soft delete
        builder.Property(a => a.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(a => a.DeletedAt).HasColumnName("deleted_at");
        builder.Property(a => a.DeletedBy).HasColumnName("deleted_by");
        builder.HasQueryFilter(a => !a.IsDeleted);

        // Indexes
        builder.HasIndex(a => new { a.OutboundOrderId, a.OutboundOrderLineId });
        builder.HasIndex(a => new { a.FacilityId, a.ItemId, a.Status });

        // Relationships
        builder.HasOne(a => a.Item).WithMany().HasForeignKey(a => a.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Lot).WithMany().HasForeignKey(a => a.LotId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(a => a.OutboundOrder).WithMany().HasForeignKey(a => a.OutboundOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.OutboundOrderLine).WithMany().HasForeignKey(a => a.OutboundOrderLineId).OnDelete(DeleteBehavior.Restrict);
    }
}

