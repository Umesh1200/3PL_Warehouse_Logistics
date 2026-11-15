using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class SkuVelocityClassificationRecordConfiguration : IEntityTypeConfiguration<SkuVelocityClassificationRecord>
{
    public void Configure(EntityTypeBuilder<SkuVelocityClassificationRecord> builder)
    {
        builder.ToTable("sku_velocity_classification_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("record_id")
            .ValueGeneratedNever();

        builder.Property(r => r.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(r => r.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(r => r.ItemId).HasColumnName("item_id").IsRequired();
        builder.Property(r => r.Sku).HasColumnName("sku").HasMaxLength(100).IsRequired();

        builder.Property(r => r.AbcClass)
            .HasColumnName("abc_class")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.CriteriaUsed)
            .HasColumnName("criteria_used")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.RecordType)
            .HasColumnName("record_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.ClassificationRunId)
            .HasColumnName("classification_run_id")
            .IsRequired(false);

        builder.Property(r => r.VelocityCategory)
            .HasColumnName("velocity_category")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.ObservationWindow)
            .HasColumnName("observation_window")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.CalculationPeriodStartUtc).HasColumnName("period_start_utc").IsRequired();
        builder.Property(r => r.CalculationPeriodEndUtc).HasColumnName("period_end_utc").IsRequired();

        builder.Property(r => r.TotalPickTasksCount).HasColumnName("total_pick_tasks_count").IsRequired();
        builder.Property(r => r.TotalUnitsDemanded).HasColumnName("total_units_demanded").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.TotalDemandValue).HasColumnName("total_demand_value").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.PeriodDemandValue).HasColumnName("period_demand_value").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.AnnualizedConsumptionValue).HasColumnName("annualized_consumption_value").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.OrderLinesCount).HasColumnName("order_lines_count").IsRequired();
        builder.Property(r => r.TotalDemandEventsCount).HasColumnName("total_demand_events_count").IsRequired();

        builder.Property(r => r.TotalMovementEventsCount).HasColumnName("total_movement_events_count").IsRequired();
        builder.Property(r => r.TotalUnitsMoved).HasColumnName("total_units_moved").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.TotalReplenishmentCount).HasColumnName("total_replenishment_count").IsRequired();
        builder.Property(r => r.TotalRelocationCount).HasColumnName("total_relocation_count").IsRequired();

        builder.Property(r => r.CostOfGoodsConsumed).HasColumnName("cost_of_goods_consumed").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.AverageInventoryValue).HasColumnName("average_inventory_value").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.InventoryTurnoverRatio).HasColumnName("inventory_turnover_ratio").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.DaysOfSupply).HasColumnName("days_of_supply").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.DaysOfSupplyFinancial).HasColumnName("days_of_supply_financial").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.DaysOfSupplyOperational).HasColumnName("days_of_supply_operational").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.CurrentOnHandUnits).HasColumnName("current_on_hand_units").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.IsDemandSpike).HasColumnName("is_demand_spike").IsRequired();
        builder.Property(r => r.IsSeasonalPattern).HasColumnName("is_seasonal_pattern").IsRequired();

        builder.Property(r => r.CumulativePercentage).HasColumnName("cumulative_percentage").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.RankInPortfolio).HasColumnName("rank_in_portfolio").IsRequired();
        builder.Property(r => r.PolicyId).HasColumnName("policy_id").IsRequired(false);
        builder.Property(r => r.CompositeScore).HasColumnName("composite_score").HasPrecision(18, 4).IsRequired();
        builder.Property(r => r.ClassAThresholdPercent).HasColumnName("class_a_threshold_percent").HasPrecision(18, 2).IsRequired(false);
        builder.Property(r => r.ClassBThresholdPercent).HasColumnName("class_b_threshold_percent").HasPrecision(18, 2).IsRequired(false);
        builder.Property(r => r.ValuationBasisUsed)
            .HasColumnName("valuation_basis_used")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(r => r.LocationId).HasColumnName("location_id").IsRequired(false);
        builder.Property(r => r.ZoneId).HasColumnName("zone_id").IsRequired(false);
        builder.Property(r => r.Aisle).HasColumnName("aisle").HasMaxLength(50).IsRequired(false);

        builder.Property(r => r.Version).HasColumnName("version").IsRequired();
        builder.Property(r => r.CalculatedAtUtc).HasColumnName("calculated_at_utc").IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(r => new { r.FacilityId, r.ClientId, r.ItemId, r.ObservationWindow, r.CriteriaUsed, r.RecordType, r.Version })
            .HasDatabaseName("ix_sku_velocity_records_facility_client_item_window_criteria_type_version")
            .IsUnique();
    }
}
