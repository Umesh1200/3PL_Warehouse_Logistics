using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class OutboundOrderLineConfiguration : IEntityTypeConfiguration<OutboundOrderLine>
{
    public void Configure(EntityTypeBuilder<OutboundOrderLine> builder)
    {
        builder.ToTable("outbound_order_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("line_id").ValueGeneratedNever();

        builder.Property(l => l.OutboundOrderId).HasColumnName("order_id").IsRequired();
        builder.Property(l => l.ItemId).HasColumnName("item_id").IsRequired();
        builder.Property(l => l.PackagingUnitId).HasColumnName("packaging_unit_id");

        builder.Property(l => l.LineNumber).HasColumnName("line_number").IsRequired();
        builder.Property(l => l.QuantityOrdered).HasColumnName("quantity_ordered").HasPrecision(14, 4).IsRequired();
        builder.Property(l => l.QuantityAllocated).HasColumnName("quantity_allocated").HasPrecision(14, 4).IsRequired();
        builder.Property(l => l.QuantityShipped).HasColumnName("quantity_shipped").HasPrecision(14, 4).IsRequired();

        builder.Property(l => l.RequiredLotNumber).HasColumnName("required_lot_number").HasMaxLength(100);
        builder.Property(l => l.MinExpiryDateUtc).HasColumnName("min_expiry_date_utc");
        builder.Property(l => l.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(l => l.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.Property(l => l.Version).HasColumnName("version").IsConcurrencyToken();

        // Soft delete
        builder.Property(l => l.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(l => l.DeletedAt).HasColumnName("deleted_at");
        builder.Property(l => l.DeletedBy).HasColumnName("deleted_by");
        builder.HasQueryFilter(l => !l.IsDeleted);

        // Indexes
        builder.HasIndex(l => new { l.OutboundOrderId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => l.ItemId);

        // Relationships
        builder.HasOne(l => l.Item).WithMany().HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.PackagingUnit).WithMany().HasForeignKey(l => l.PackagingUnitId).OnDelete(DeleteBehavior.SetNull);
    }
}
