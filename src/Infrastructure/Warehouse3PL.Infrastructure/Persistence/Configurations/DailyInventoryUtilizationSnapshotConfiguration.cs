using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyInventoryUtilizationSnapshotConfiguration : IEntityTypeConfiguration<DailyInventoryUtilizationSnapshot>
{
    public void Configure(EntityTypeBuilder<DailyInventoryUtilizationSnapshot> builder)
    {
        builder.ToTable("daily_inventory_utilization_snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("snapshot_id")
            .ValueGeneratedNever();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id");

        builder.Property(s => s.DateUtc)
            .HasColumnName("date_utc")
            .IsRequired();

        builder.Property(s => s.TimeGrain)
            .HasColumnName("time_grain")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Physical Capacity & Occupancy
        builder.Property(s => s.TotalStorageLocations).HasColumnName("total_storage_locations").IsRequired();
        builder.Property(s => s.OccupiedStorageLocations).HasColumnName("occupied_storage_locations").IsRequired();
        builder.Property(s => s.PalletPositionOccupancyPercent).HasColumnName("pallet_position_occupancy_percent").HasPrecision(18, 2).IsRequired();

        // Volumetric Cube Fill
        builder.Property(s => s.TotalCapacityCubicMeters).HasColumnName("total_capacity_cubic_meters").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.OccupiedCubicMeters).HasColumnName("occupied_cubic_meters").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.VolumetricCubeFillPercent).HasColumnName("volumetric_cube_fill_percent").HasPrecision(18, 2).IsRequired();

        // Inventory Composition & Dead Capital
        builder.Property(s => s.TotalActiveSkus).HasColumnName("total_active_skus").IsRequired();
        builder.Property(s => s.TotalInventoryUnits).HasColumnName("total_inventory_units").IsRequired();
        builder.Property(s => s.TotalInventoryValue).HasColumnName("total_inventory_value").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.DeadStockSkusCount).HasColumnName("dead_stock_skus_count").IsRequired();
        builder.Property(s => s.DeadStockUnitsCount).HasColumnName("dead_stock_units_count").IsRequired();
        builder.Property(s => s.DeadStockValue).HasColumnName("dead_stock_value").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.DeadStockRatioPercent).HasColumnName("dead_stock_ratio_percent").HasPrecision(18, 2).IsRequired();

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee
        builder.HasIndex(s => new { s.FacilityId, s.ClientId, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_daily_inv_utilization_fac_client_date_grain")
            .IsUnique();

        builder.HasIndex(s => new { s.FacilityId, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_daily_inv_utilization_fac_date_grain");
    }
}

