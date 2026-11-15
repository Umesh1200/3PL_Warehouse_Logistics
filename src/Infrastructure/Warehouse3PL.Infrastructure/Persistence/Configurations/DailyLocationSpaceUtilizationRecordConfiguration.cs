using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyLocationSpaceUtilizationRecordConfiguration : IEntityTypeConfiguration<DailyLocationSpaceUtilizationRecord>
{
    public void Configure(EntityTypeBuilder<DailyLocationSpaceUtilizationRecord> builder)
    {
        builder.ToTable("daily_location_space_utilization_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("record_id")
            .ValueGeneratedNever();

        builder.Property(r => r.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(r => r.ZoneId).HasColumnName("zone_id").IsRequired(false);
        builder.Property(r => r.LocationId).HasColumnName("location_id").IsRequired();
        builder.Property(r => r.LocationCode).HasColumnName("location_code").HasMaxLength(100).IsRequired();

        builder.Property(r => r.LocationType)
            .HasColumnName("location_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.SnapshotDateUtc).HasColumnName("snapshot_date_utc").IsRequired();

        // Coordinates
        builder.Property(r => r.AisleCode).HasColumnName("aisle_code").HasMaxLength(50).IsRequired(false);
        builder.Property(r => r.RackCode).HasColumnName("rack_code").HasMaxLength(50).IsRequired(false);
        builder.Property(r => r.ShelfLevel).HasColumnName("shelf_level").IsRequired(false);
        builder.Property(r => r.BinNumber).HasColumnName("bin_number").IsRequired(false);
        builder.Property(r => r.XCoord).HasColumnName("x_coord").HasPrecision(18, 4).IsRequired(false);
        builder.Property(r => r.YCoord).HasColumnName("y_coord").HasPrecision(18, 4).IsRequired(false);
        builder.Property(r => r.ZCoord).HasColumnName("z_coord").HasPrecision(18, 4).IsRequired(false);

        // Volumetric Fill
        builder.Property(r => r.PhysicalCubicMeters).HasColumnName("physical_cubic_meters").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.UsableCapacityFactor).HasColumnName("usable_capacity_factor").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.UsableCubicMeters).HasColumnName("usable_cubic_meters").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.OccupiedCubicMeters).HasColumnName("occupied_cubic_meters").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.AvailableCubicMeters).HasColumnName("available_cubic_meters").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.CubeUtilizationPercent).HasColumnName("cube_utilization_percent").HasPrecision(18, 2).IsRequired();

        // Pallet Occupancy
        builder.Property(r => r.PhysicalPalletPositions).HasColumnName("physical_pallet_positions").IsRequired();
        builder.Property(r => r.UsablePalletPositions).HasColumnName("usable_pallet_positions").IsRequired();
        builder.Property(r => r.OccupiedPalletPositions).HasColumnName("occupied_pallet_positions").IsRequired();
        builder.Property(r => r.AvailablePalletPositions).HasColumnName("available_pallet_positions").IsRequired();
        builder.Property(r => r.PalletOccupancyPercent).HasColumnName("pallet_occupancy_percent").HasPrecision(18, 2).IsRequired();

        // Operational & Blocked State
        builder.Property(r => r.IsBlocked).HasColumnName("is_blocked").IsRequired();
        builder.Property(r => r.BlockReason).HasColumnName("block_reason").HasMaxLength(250).IsRequired(false);

        // Capacity
        builder.Property(r => r.MaxWeightKg).HasColumnName("max_weight_kg").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.OccupiedWeightKg).HasColumnName("occupied_weight_kg").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.WeightUtilizationPercent).HasColumnName("weight_utilization_percent").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.IsWeightConstrained).HasColumnName("is_weight_constrained").IsRequired();

        // Over-capacity Detection
        builder.Property(r => r.IsOverCapacity).HasColumnName("is_over_capacity").IsRequired();
        builder.Property(r => r.OverCapacityExcessCbm).HasColumnName("over_capacity_excess_cbm").HasPrecision(18, 4).IsRequired();

        builder.Property(r => r.DistinctSkuCount).HasColumnName("distinct_sku_count").IsRequired();
        builder.Property(r => r.TotalUnitsStored).HasColumnName("total_units_stored").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.HasMissingDimensionsFallback).HasColumnName("has_missing_dimensions_fallback").IsRequired();

        // Partitioned Quantities by Status
        builder.Property(r => r.UnitsAvailable).HasColumnName("units_available").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.UnitsAllocated).HasColumnName("units_allocated").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.UnitsReserved).HasColumnName("units_reserved").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.UnitsQuarantined).HasColumnName("units_quarantined").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.UnitsDamaged).HasColumnName("units_damaged").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.UnitsOnHold).HasColumnName("units_on_hold").HasPrecision(18, 4).IsRequired();

        // Partitioned Occupied Volume (CBM)
        builder.Property(r => r.OccupiedCbmAvailable).HasColumnName("occupied_cbm_available").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.OccupiedCbmAllocated).HasColumnName("occupied_cbm_allocated").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.OccupiedCbmReserved).HasColumnName("occupied_cbm_reserved").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.OccupiedCbmQuarantined).HasColumnName("occupied_cbm_quarantined").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.OccupiedCbmDamaged).HasColumnName("occupied_cbm_damaged").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.OccupiedCbmOnHold).HasColumnName("occupied_cbm_on_hold").HasPrecision(18, 4).IsRequired();

        builder.Property(r => r.SpaceClassification)
            .HasColumnName("space_classification")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(r => new { r.FacilityId, r.SnapshotDateUtc, r.LocationId })
            .HasDatabaseName("ix_daily_location_space_facility_date_location");

        builder.HasIndex(r => new { r.FacilityId, r.ZoneId, r.SnapshotDateUtc })
            .HasDatabaseName("ix_daily_location_space_facility_zone_date");
    }
}

