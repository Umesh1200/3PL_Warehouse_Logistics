using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PickExceptionConfiguration : IEntityTypeConfiguration<PickException>
{
    public void Configure(EntityTypeBuilder<PickException> builder)
    {
        builder.ToTable("pick_exceptions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("exception_id")
            .ValueGeneratedNever();

        builder.Property(e => e.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(e => e.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(e => e.PickTaskId)
            .HasColumnName("pick_task_id")
            .IsRequired();

        builder.Property(e => e.OutboundOrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(e => e.OutboundOrderLineId)
            .HasColumnName("line_id")
            .IsRequired();

        builder.Property(e => e.LocationId)
            .HasColumnName("location_id")
            .IsRequired();

        builder.Property(e => e.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(e => e.LotId)
            .HasColumnName("lot_id");

        builder.Property(e => e.ExpectedQuantity)
            .HasColumnName("expected_quantity")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(e => e.ActualQuantityPicked)
            .HasColumnName("actual_quantity_picked")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Ignore(e => e.DiscrepancyQuantity);

        builder.Property(e => e.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(e => e.ReportedByUserId)
            .HasColumnName("reported_by_user_id")
            .IsRequired();

        builder.Property(e => e.ReportedAtUtc)
            .HasColumnName("reported_at_utc")
            .IsRequired();

        builder.Property(e => e.GeneratedCycleCountId)
            .HasColumnName("generated_cycle_count_id");

        builder.Property(e => e.LocationLockApplied)
            .HasColumnName("location_lock_applied")
            .IsRequired();

        // Indexes
        builder.HasIndex(e => new { e.ClientId, e.FacilityId });
        builder.HasIndex(e => e.PickTaskId);
        builder.HasIndex(e => e.LocationId);
        builder.HasIndex(e => e.ItemId);
        builder.HasIndex(e => e.GeneratedCycleCountId);

        // Relationships
        builder.HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Facility)
            .WithMany()
            .HasForeignKey(e => e.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PickTask)
            .WithMany(t => t.Exceptions)
            .HasForeignKey(e => e.PickTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.OutboundOrder)
            .WithMany()
            .HasForeignKey(e => e.OutboundOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OutboundOrderLine)
            .WithMany()
            .HasForeignKey(e => e.OutboundOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Item)
            .WithMany()
            .HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Lot)
            .WithMany()
            .HasForeignKey(e => e.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.GeneratedCycleCount)
            .WithMany()
            .HasForeignKey(e => e.GeneratedCycleCountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

