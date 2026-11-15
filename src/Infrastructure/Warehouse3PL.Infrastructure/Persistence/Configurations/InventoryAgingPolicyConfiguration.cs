using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InventoryAgingPolicyConfiguration : IEntityTypeConfiguration<InventoryAgingPolicy>
{
    public void Configure(EntityTypeBuilder<InventoryAgingPolicy> builder)
    {
        builder.ToTable("inventory_aging_policies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("policy_id")
            .ValueGeneratedNever();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired(false);

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired(false);

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(p => p.SlowMovingThresholdDays).HasColumnName("slow_moving_threshold_days").IsRequired();
        builder.Property(p => p.DormantThresholdDays).HasColumnName("dormant_threshold_days").IsRequired();
        builder.Property(p => p.DeadStockThresholdDays).HasColumnName("dead_stock_threshold_days").IsRequired();
        builder.Property(p => p.NearExpiryThresholdDays).HasColumnName("near_expiry_threshold_days").IsRequired();
        builder.Property(p => p.CriticalExpiryThresholdDays).HasColumnName("critical_expiry_threshold_days").IsRequired();

        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(10).IsRequired();
        builder.Property(p => p.DailyHoldingCostPerCbm).HasColumnName("daily_holding_cost_per_cbm").HasPrecision(18, 4).IsRequired();
        builder.Property(p => p.DailyHoldingCostPerPallet).HasColumnName("daily_holding_cost_per_pallet").HasPrecision(18, 4).IsRequired();

        builder.Property(p => p.DefaultBasis)
            .HasColumnName("default_basis")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Version).HasColumnName("version").IsRequired();
        builder.Property(p => p.EffectiveFromUtc).HasColumnName("effective_from_utc").IsRequired();
        builder.Property(p => p.EffectiveToUtc).HasColumnName("effective_to_utc").IsRequired(false);
        builder.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(p => new { p.FacilityId, p.ClientId, p.IsActive })
            .HasDatabaseName("ix_inventory_aging_policies_facility_client");
    }
}

