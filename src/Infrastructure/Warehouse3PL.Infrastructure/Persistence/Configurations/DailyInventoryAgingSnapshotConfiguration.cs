using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyInventoryAgingSnapshotConfiguration : IEntityTypeConfiguration<DailyInventoryAgingSnapshot>
{
    public void Configure(EntityTypeBuilder<DailyInventoryAgingSnapshot> builder)
    {
        builder.ToTable("daily_inventory_aging_snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("snapshot_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(s => s.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(s => s.LocationId).HasColumnName("location_id").IsRequired();
        builder.Property(s => s.LocationCode).HasColumnName("location_code").HasMaxLength(100).IsRequired();
        builder.Property(s => s.ItemId).HasColumnName("item_id").IsRequired();
        builder.Property(s => s.Sku).HasColumnName("sku").HasMaxLength(100).IsRequired();

        builder.Property(s => s.LotId).HasColumnName("lot_id").IsRequired(false);
        builder.Property(s => s.LotNumber).HasColumnName("lot_number").HasMaxLength(100).IsRequired(false);
        builder.Property(s => s.LpnCode).HasColumnName("lpn_code").HasMaxLength(100).IsRequired(false);

        builder.Property(s => s.SnapshotDateUtc).HasColumnName("snapshot_date_utc").IsRequired();

        // Inventory Holding Status & Partitioned Quantities
        builder.Property(s => s.HoldingStatus)
            .HasColumnName("holding_status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.QtyAvailable).HasColumnName("qty_available").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.QtyAllocated).HasColumnName("qty_allocated").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.QtyReserved).HasColumnName("qty_reserved").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.QtyQuarantined).HasColumnName("qty_quarantined").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.QtyDamaged).HasColumnName("qty_damaged").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.QtyOnHold).HasColumnName("qty_on_hold").HasPrecision(18, 4).IsRequired();

        // Five Independent Temporal Dimensions
        builder.Property(s => s.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired(false);
        builder.Property(s => s.ReceiptAgeDays).HasColumnName("receipt_age_days").IsRequired();

        builder.Property(s => s.LastMovedAtUtc).HasColumnName("last_moved_at_utc").IsRequired(false);
        builder.Property(s => s.LastMovementAgeDays).HasColumnName("last_movement_age_days").IsRequired();

        builder.Property(s => s.LastDemandedAtUtc).HasColumnName("last_demanded_at_utc").IsRequired(false);
        builder.Property(s => s.LastDemandAgeDays).HasColumnName("last_demand_age_days").IsRequired();
        builder.Property(s => s.DemandInactivityDays).HasColumnName("demand_inactivity_days").IsRequired(false);

        builder.Property(s => s.ExpirationDateUtc).HasColumnName("expiration_date_utc").IsRequired(false);
        builder.Property(s => s.DaysUntilExpiry).HasColumnName("days_until_expiry").IsRequired(false);

        builder.Property(s => s.HoldingDurationDays).HasColumnName("holding_duration_days").IsRequired();

        // Categorization
        builder.Property(s => s.AgingBucket)
            .HasColumnName("aging_bucket")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.VelocityCategory)
            .HasColumnName("velocity_category")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Flags
        builder.Property(s => s.IsAged).HasColumnName("is_aged").IsRequired();
        builder.Property(s => s.IsSlowMoving).HasColumnName("is_slow_moving").IsRequired();
        builder.Property(s => s.IsNonMoving).HasColumnName("is_non_moving").IsRequired();
        builder.Property(s => s.IsDormant).HasColumnName("is_dormant").IsRequired();
        builder.Property(s => s.IsDeadStock).HasColumnName("is_dead_stock").IsRequired();
        builder.Property(s => s.IsNearExpiry).HasColumnName("is_near_expiry").IsRequired();
        builder.Property(s => s.IsCriticalExpiry).HasColumnName("is_critical_expiry").IsRequired();
        builder.Property(s => s.IsExpired).HasColumnName("is_expired").IsRequired();

        // Quantities & Volumes & Financials
        builder.Property(s => s.QuantityOnHand).HasColumnName("quantity_on_hand").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.TotalInventoryValue).HasColumnName("total_inventory_value").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(10).IsRequired();
        builder.Property(s => s.DailyHoldingCost).HasColumnName("daily_holding_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.AccruedHoldingCost).HasColumnName("accrued_holding_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.OccupiedCbm).HasColumnName("occupied_cbm").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.PalletFootprint).HasColumnName("pallet_footprint").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.HasMissingDimensionsFallback).HasColumnName("has_missing_dimensions_fallback").IsRequired();
        builder.Property(s => s.SourceInventoryReference).HasColumnName("source_inventory_reference").HasMaxLength(200).IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(s => new { s.FacilityId, s.ClientId, s.SnapshotDateUtc, s.ItemId })
            .HasDatabaseName("ix_daily_inventory_aging_snapshots_facility_client_date_item");

        builder.HasIndex(s => new { s.FacilityId, s.SnapshotDateUtc, s.AgingBucket })
            .HasDatabaseName("ix_daily_inventory_aging_snapshots_facility_date_bucket");
    }
}

