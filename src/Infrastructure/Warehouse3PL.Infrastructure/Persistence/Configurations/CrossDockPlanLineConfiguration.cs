using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.CrossDock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CrossDockPlanLineConfiguration : IEntityTypeConfiguration<CrossDockPlanLine>
{
    public void Configure(EntityTypeBuilder<CrossDockPlanLine> builder)
    {
        builder.ToTable("cross_dock_plan_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("plan_line_id").ValueGeneratedNever();

        builder.Property(l => l.CrossDockPlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(l => l.ItemId).HasColumnName("item_id").IsRequired();
        builder.Property(l => l.ReceiptLineId).HasColumnName("receipt_line_id");
        builder.Property(l => l.InboundLpn).HasColumnName("inbound_lpn").HasMaxLength(100);
        builder.Property(l => l.SourceLocationId).HasColumnName("source_location_id").IsRequired();
        builder.Property(l => l.TargetLocationId).HasColumnName("target_location_id").IsRequired();
        builder.Property(l => l.InboundAvailableQuantity).HasColumnName("inbound_available_qty").HasPrecision(14, 4).IsRequired();
        builder.Property(l => l.OutboundDemandQuantity).HasColumnName("outbound_demand_qty").HasPrecision(14, 4).IsRequired();
        builder.Property(l => l.PlannedQuantity).HasColumnName("planned_qty").HasPrecision(14, 4).IsRequired();
        builder.Property(l => l.ExecutedQuantity).HasColumnName("executed_qty").HasPrecision(14, 4).IsRequired();
        builder.Property(l => l.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(l => l.Version).HasColumnName("version").IsConcurrencyToken();

        // Soft delete
        builder.Property(l => l.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(l => l.DeletedAt).HasColumnName("deleted_at");
        builder.Property(l => l.DeletedBy).HasColumnName("deleted_by");
        builder.HasQueryFilter(l => !l.IsDeleted);

        // Indexes
        builder.HasIndex(l => new { l.CrossDockPlanId, l.ItemId });
        builder.HasIndex(l => l.InboundLpn);

        // Relationships
        builder.HasOne(l => l.Item).WithMany().HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.ReceiptLine).WithMany().HasForeignKey(l => l.ReceiptLineId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(l => l.SourceLocation).WithMany().HasForeignKey(l => l.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.TargetLocation).WithMany().HasForeignKey(l => l.TargetLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(l => l.Allocations).WithOne(a => a.CrossDockPlanLine).HasForeignKey(a => a.CrossDockPlanLineId).OnDelete(DeleteBehavior.Cascade);
    }
}

