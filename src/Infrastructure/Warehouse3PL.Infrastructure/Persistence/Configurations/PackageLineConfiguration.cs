using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PackageLineConfiguration : IEntityTypeConfiguration<PackageLine>
{
    public void Configure(EntityTypeBuilder<PackageLine> builder)
    {
        builder.ToTable("package_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("package_line_id")
            .ValueGeneratedNever();

        builder.Property(l => l.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(l => l.OutboundOrderLineId)
            .HasColumnName("outbound_order_line_id")
            .IsRequired();

        builder.Property(l => l.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(l => l.LotId)
            .HasColumnName("lot_id");

        builder.Property(l => l.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(l => l.SourceHandlingUnitId)
            .HasColumnName("source_handling_unit_id");

        builder.Property(l => l.InventoryAllocationId)
            .HasColumnName("allocation_id")
            .IsRequired();

        // Indexes
        builder.HasIndex(l => l.PackageId);
        builder.HasIndex(l => l.OutboundOrderLineId);
        builder.HasIndex(l => l.ItemId);
        builder.HasIndex(l => l.InventoryAllocationId);
        builder.HasIndex(l => l.SourceHandlingUnitId);

        // Relationships
        builder.HasOne(l => l.Package)
            .WithMany(p => p.Lines)
            .HasForeignKey(l => l.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.OutboundOrderLine)
            .WithMany()
            .HasForeignKey(l => l.OutboundOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Item)
            .WithMany()
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Lot)
            .WithMany()
            .HasForeignKey(l => l.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.SourceHandlingUnit)
            .WithMany()
            .HasForeignKey(l => l.SourceHandlingUnitId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.InventoryAllocation)
            .WithMany()
            .HasForeignKey(l => l.InventoryAllocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

