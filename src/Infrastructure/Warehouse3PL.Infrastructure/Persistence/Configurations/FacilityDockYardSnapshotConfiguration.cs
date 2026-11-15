using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class FacilityDockYardSnapshotConfiguration : IEntityTypeConfiguration<FacilityDockYardSnapshot>
{
    public void Configure(EntityTypeBuilder<FacilityDockYardSnapshot> builder)
    {
        builder.ToTable("facility_dock_yard_snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("snapshot_id")
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

        // Capacity & Occupancy
        builder.Property(s => s.TotalDockDoorsCount).HasColumnName("total_dock_doors_count").IsRequired();
        builder.Property(s => s.OccupiedDockDoorsCount).HasColumnName("occupied_dock_doors_count").IsRequired();
        builder.Property(s => s.DockDoorUtilizationPercent).HasColumnName("dock_door_utilization_percent").HasPrecision(18, 2).IsRequired();

        builder.Property(s => s.TotalYardSpotsCount).HasColumnName("total_yard_spots_count").IsRequired();
        builder.Property(s => s.OccupiedYardSpotsCount).HasColumnName("occupied_yard_spots_count").IsRequired();
        builder.Property(s => s.YardOccupancyPercent).HasColumnName("yard_occupancy_percent").HasPrecision(18, 2).IsRequired();

        // Vehicle Counts
        builder.Property(s => s.TrucksCheckedInCount).HasColumnName("trucks_checked_in_count").IsRequired();
        builder.Property(s => s.TrucksDepartedCount).HasColumnName("trucks_departed_count").IsRequired();
        builder.Property(s => s.TrucksWaitingInYardCount).HasColumnName("trucks_waiting_in_yard_count").IsRequired();
        builder.Property(s => s.TrucksAtDockCount).HasColumnName("trucks_at_dock_count").IsRequired();

        // Precision Turn-Time
        builder.Property(s => s.TotalYardWaitMinutesSum).HasColumnName("total_yard_wait_minutes_sum").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalYardWaitTrucksCount).HasColumnName("total_yard_wait_trucks_count").IsRequired();
        builder.Property(s => s.AverageYardWaitTimeMinutes).HasColumnName("average_yard_wait_time_minutes").HasPrecision(18, 2).IsRequired();

        builder.Property(s => s.TotalDockServiceMinutesSum).HasColumnName("total_dock_service_minutes_sum").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalDockServiceTrucksCount).HasColumnName("total_dock_service_trucks_count").IsRequired();
        builder.Property(s => s.AverageDockServiceTimeMinutes).HasColumnName("average_dock_service_time_minutes").HasPrecision(18, 2).IsRequired();

        builder.Property(s => s.TotalTurnTimeMinutesSum).HasColumnName("total_turn_time_minutes_sum").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalDepartedTrucksCount).HasColumnName("total_departed_trucks_count").IsRequired();
        builder.Property(s => s.AverageTotalTurnTimeMinutes).HasColumnName("average_total_turn_time_minutes").HasPrecision(18, 2).IsRequired();

        builder.Property(s => s.ProjectionVersion).HasColumnName("projection_version").IsRequired();

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee: Unique bucket per facility, date, and grain
        builder.HasIndex(s => new { s.FacilityId, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_facility_dock_yard_fac_date_grain")
            .IsUnique();

        builder.HasIndex(s => new { s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_facility_dock_yard_date_grain");
    }
}

