using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyFacilityProductivitySummaryConfiguration : IEntityTypeConfiguration<DailyFacilityProductivitySummary>
{
    public void Configure(EntityTypeBuilder<DailyFacilityProductivitySummary> builder)
    {
        builder.ToTable("daily_facility_productivity_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("summary_id")
            .ValueGeneratedNever();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.DateUtc)
            .HasColumnName("date_utc")
            .IsRequired();

        builder.Property(s => s.TimeGrain)
            .HasColumnName("time_grain")
            .HasConversion<string>()
            .HasMaxLength(30)
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

        // Labor & Rates
        builder.Property(s => s.ActiveWorkerHeadcount).HasColumnName("active_worker_headcount").IsRequired();
        builder.Property(s => s.TotalLaborHours).HasColumnName("total_labor_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalPickHours).HasColumnName("total_pick_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalPutawayHours).HasColumnName("total_putaway_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.UnitsPerLaborHour).HasColumnName("units_per_labor_hour").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.PutawayPerLaborHour).HasColumnName("putaway_per_labor_hour").HasPrecision(18, 2).IsRequired();

        // Fulfillment Velocity & SLA
        builder.Property(s => s.AverageFulfillmentCycleTimeHours).HasColumnName("average_fulfillment_cycle_time_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalCycleTimeOrdersCount).HasColumnName("total_cycle_time_orders_count").IsRequired();
        builder.Property(s => s.TotalCycleTimeHoursSum).HasColumnName("total_cycle_time_hours_sum").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.EligibleOrdersCutoffCount).HasColumnName("eligible_orders_cutoff_count").IsRequired();
        builder.Property(s => s.SameDayDispatchedCutoffCount).HasColumnName("same_day_dispatched_cutoff_count").IsRequired();
        builder.Property(s => s.SameDayDispatchAdherencePercent).HasColumnName("same_day_dispatch_adherence_percent").HasPrecision(18, 2).IsRequired();

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee: Unique bucket per facility, date, and grain
        builder.HasIndex(s => new { s.FacilityId, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_daily_facility_productivity_facility_date_grain")
            .IsUnique();

        builder.HasIndex(s => new { s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_daily_facility_productivity_date_grain");
    }
}

