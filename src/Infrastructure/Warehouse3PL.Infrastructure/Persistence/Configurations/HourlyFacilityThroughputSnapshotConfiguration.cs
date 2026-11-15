using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class HourlyFacilityThroughputSnapshotConfiguration : IEntityTypeConfiguration<HourlyFacilityThroughputSnapshot>
{
    public void Configure(EntityTypeBuilder<HourlyFacilityThroughputSnapshot> builder)
    {
        builder.ToTable("hourly_facility_throughput_snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("snapshot_id")
            .ValueGeneratedNever();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.HourTimestampUtc)
            .HasColumnName("hour_timestamp_utc")
            .IsRequired();

        // Throughput counts
        builder.Property(s => s.OrdersReceivedCount).HasColumnName("orders_received_count").IsRequired();
        builder.Property(s => s.OrdersAllocatedCount).HasColumnName("orders_allocated_count").IsRequired();
        builder.Property(s => s.LinesPickedCount).HasColumnName("lines_picked_count").IsRequired();
        builder.Property(s => s.UnitsPickedCount).HasColumnName("units_picked_count").IsRequired();
        builder.Property(s => s.CartonsPackedCount).HasColumnName("cartons_packed_count").IsRequired();
        builder.Property(s => s.OrdersDispatchedCount).HasColumnName("orders_dispatched_count").IsRequired();
        builder.Property(s => s.PackagesDispatchedCount).HasColumnName("packages_dispatched_count").IsRequired();
        builder.Property(s => s.InboundPalletsReceivedCount).HasColumnName("inbound_pallets_received_count").IsRequired();
        builder.Property(s => s.InboundPutawayCompletedCount).HasColumnName("inbound_putaway_completed_count").IsRequired();
        builder.Property(s => s.ReturnsInspectedCount).HasColumnName("returns_inspected_count").IsRequired();

        // Labor & Pacing
        builder.Property(s => s.ActiveWorkerHeadcount).HasColumnName("active_worker_headcount").IsRequired();
        builder.Property(s => s.TotalLaborHours).HasColumnName("total_labor_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.PickDurationHours).HasColumnName("pick_duration_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.PutawayDurationHours).HasColumnName("putaway_duration_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.UnitsPerLaborHour).HasColumnName("units_per_labor_hour").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.PutawayPerLaborHour).HasColumnName("putaway_per_labor_hour").HasPrecision(18, 2).IsRequired();

        // Cycle time & Cutoff
        builder.Property(s => s.AverageCycleTimeHours).HasColumnName("average_cycle_time_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalCycleOrdersCount).HasColumnName("total_cycle_orders_count").IsRequired();
        builder.Property(s => s.TotalCycleHoursSum).HasColumnName("total_cycle_hours_sum").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.EligibleCutoffCount).HasColumnName("eligible_cutoff_count").IsRequired();
        builder.Property(s => s.SameDayDispatchedCount).HasColumnName("same_day_dispatched_count").IsRequired();
        builder.Property(s => s.SameDayDispatchAdherencePercent).HasColumnName("same_day_dispatch_adherence_percent").HasPrecision(18, 2).IsRequired();

        builder.Property(s => s.ProjectionVersion).HasColumnName("projection_version").IsRequired();

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee: Exactly one hourly bucket per facility and hour
        builder.HasIndex(s => new { s.FacilityId, s.HourTimestampUtc })
            .HasDatabaseName("ix_hourly_facility_throughput_fac_hour")
            .IsUnique();

        builder.HasIndex(s => s.HourTimestampUtc)
            .HasDatabaseName("ix_hourly_facility_throughput_hour");
    }
}

