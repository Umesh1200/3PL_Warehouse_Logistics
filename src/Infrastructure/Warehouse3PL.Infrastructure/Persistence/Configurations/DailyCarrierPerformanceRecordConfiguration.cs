using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyCarrierPerformanceRecordConfiguration : IEntityTypeConfiguration<DailyCarrierPerformanceRecord>
{
    public void Configure(EntityTypeBuilder<DailyCarrierPerformanceRecord> builder)
    {
        builder.ToTable("daily_carrier_performance_records");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("record_id")
            .ValueGeneratedNever();

        builder.Property(s => s.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired(false);

        builder.Property(s => s.DateUtc)
            .HasColumnName("date_utc")
            .IsRequired();

        builder.Property(s => s.TimeGrain)
            .HasColumnName("time_grain")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Shipment Volume & Status
        builder.Property(s => s.DispatchedShipmentsCount).HasColumnName("dispatched_shipments_count").IsRequired();
        builder.Property(s => s.DeliveredShipmentsCount).HasColumnName("delivered_shipments_count").IsRequired();
        builder.Property(s => s.InTransitShipmentsCount).HasColumnName("in_transit_shipments_count").IsRequired();
        builder.Property(s => s.OverdueInTransitCount).HasColumnName("overdue_in_transit_count").HasDefaultValue(0);
        builder.Property(s => s.CompletedDeliveryOutcomesCount).HasColumnName("completed_delivery_outcomes_count").HasDefaultValue(0);

        // Delivery SLA Performance
        builder.Property(s => s.OnTimeDeliveredCount).HasColumnName("on_time_delivered_count").IsRequired();
        builder.Property(s => s.LateDeliveredCount).HasColumnName("late_delivered_count").IsRequired();
        builder.Property(s => s.FirstAttemptDeliveredCount).HasColumnName("first_attempt_delivered_count").IsRequired();
        builder.Property(s => s.DeliveryExceptionsCount).HasColumnName("delivery_exceptions_count").IsRequired();

        // Rate Metrics (Nullable to represent explicit unavailable/undefined state when denominator is zero)
        builder.Property(s => s.OnTimeDeliveryRatePercent).HasColumnName("on_time_delivery_rate_percent").HasPrecision(18, 2).IsRequired(false);
        builder.Property(s => s.FirstAttemptDeliveryRatePercent).HasColumnName("first_attempt_delivery_rate_percent").HasPrecision(18, 2).IsRequired(false);
        builder.Property(s => s.DeliveryExceptionRatePercent).HasColumnName("delivery_exception_rate_percent").HasPrecision(18, 2).IsRequired(false);

        // Transit Time
        builder.Property(s => s.TotalTransitDaysSum).HasColumnName("total_transit_days_sum").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.AverageTransitDays).HasColumnName("average_transit_days").HasPrecision(18, 2).IsRequired(false);

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee
        builder.HasIndex(s => new { s.CarrierCode, s.CarrierServiceLevel, s.FacilityId, s.ClientId, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_daily_carrier_perf_carrier_svc_fac_client_date_grain")
            .IsUnique();

        builder.HasIndex(s => new { s.CarrierCode, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_daily_carrier_perf_carrier_date_grain");
    }
}

