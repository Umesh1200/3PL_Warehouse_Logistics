using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LaneTransitSummaryConfiguration : IEntityTypeConfiguration<LaneTransitSummary>
{
    public void Configure(EntityTypeBuilder<LaneTransitSummary> builder)
    {
        builder.ToTable("lane_transit_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("lane_summary_id")
            .ValueGeneratedNever();

        builder.Property(s => s.OriginFacilityId).HasColumnName("origin_facility_id").IsRequired();
        builder.Property(s => s.OriginRegion).HasColumnName("origin_region").HasMaxLength(50);
        builder.Property(s => s.DestinationCountry).HasColumnName("destination_country").HasMaxLength(10).IsRequired();
        builder.Property(s => s.DestinationStateOrRegion).HasColumnName("destination_state_or_region").HasMaxLength(50);

        builder.Property(s => s.CarrierCode).HasColumnName("carrier_code").HasMaxLength(50).IsRequired();
        builder.Property(s => s.CarrierServiceLevel).HasColumnName("carrier_service_level").HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(s => s.DateUtc).HasColumnName("date_utc").IsRequired();
        builder.Property(s => s.TimeGrain).HasColumnName("time_grain").HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(s => s.DispatchedShipmentsCount).HasColumnName("dispatched_shipments_count").IsRequired();
        builder.Property(s => s.DeliveredShipmentsCount).HasColumnName("delivered_shipments_count").IsRequired();

        builder.Property(s => s.TotalTransitDaysSum).HasColumnName("total_transit_days_sum").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.AverageTransitDays).HasColumnName("average_transit_days").HasPrecision(18, 2).IsRequired(false);
        builder.Property(s => s.MinTransitDays).HasColumnName("min_transit_days").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.MaxTransitDays).HasColumnName("max_transit_days").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.P95TransitDays).HasColumnName("p95_transit_days").HasPrecision(18, 2).IsRequired();

        builder.Property(s => s.OnTimeDeliveredCount).HasColumnName("on_time_delivered_count").IsRequired();
        builder.Property(s => s.OnTimeDeliveryRatePercent).HasColumnName("on_time_delivery_rate_percent").HasPrecision(18, 2).IsRequired(false);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(s => new { s.OriginFacilityId, s.DestinationCountry, s.DestinationStateOrRegion, s.CarrierCode, s.CarrierServiceLevel, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_lane_transit_origin_dest_carrier_svc_date_grain")
            .IsUnique();
    }
}
