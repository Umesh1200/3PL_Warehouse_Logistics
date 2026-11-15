using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class AllocationShortageConfiguration : IEntityTypeConfiguration<AllocationShortage>
{
    public void Configure(EntityTypeBuilder<AllocationShortage> builder)
    {
        builder.ToTable("allocation_shortages");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("shortage_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.OutboundOrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(s => s.OutboundOrderLineId)
            .HasColumnName("line_id")
            .IsRequired();

        builder.Property(s => s.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(s => s.QuantityRequested)
            .HasColumnName("quantity_requested")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(s => s.QuantityAllocated)
            .HasColumnName("quantity_allocated")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(s => s.ShortageQuantity)
            .HasColumnName("shortage_quantity")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(s => s.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(s => s.RecordedAtUtc)
            .HasColumnName("recorded_at_utc")
            .IsRequired();

        builder.Property(s => s.ResolvedAtUtc)
            .HasColumnName("resolved_at_utc");

        builder.Property(s => s.IsResolved)
            .HasColumnName("is_resolved")
            .IsRequired();

        // Indexes
        builder.HasIndex(s => new { s.ClientId, s.FacilityId, s.ItemId, s.IsResolved });
        builder.HasIndex(s => s.OutboundOrderId);
        builder.HasIndex(s => s.OutboundOrderLineId);

        // Relationships
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.OutboundOrder)
            .WithMany(o => o.Shortages)
            .HasForeignKey(s => s.OutboundOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.OutboundOrderLine)
            .WithMany(l => l.Shortages)
            .HasForeignKey(s => s.OutboundOrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Item)
            .WithMany()
            .HasForeignKey(s => s.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

