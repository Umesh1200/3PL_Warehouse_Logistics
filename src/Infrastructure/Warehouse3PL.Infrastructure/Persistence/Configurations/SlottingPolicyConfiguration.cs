using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class SlottingPolicyConfiguration : IEntityTypeConfiguration<SlottingPolicy>
{
    public void Configure(EntityTypeBuilder<SlottingPolicy> builder)
    {
        builder.ToTable("slotting_policies");

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

        builder.Property(p => p.ZoneId)
            .HasColumnName("zone_id")
            .IsRequired(false);

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(p => p.PrimaryGoal)
            .HasColumnName("primary_goal")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.SecondaryGoal)
            .HasColumnName("secondary_goal")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(p => p.GoldenZoneMinZMeters)
            .HasColumnName("golden_zone_min_z_meters")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.GoldenZoneMaxZMeters)
            .HasColumnName("golden_zone_max_z_meters")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.GoldenZoneShelfLevels)
            .HasColumnName("golden_zone_shelf_levels")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.TargetClassAPickFace)
            .HasColumnName("target_class_a_pick_face")
            .IsRequired();

        builder.Property(p => p.TargetClassBIntermediate)
            .HasColumnName("target_class_b_intermediate")
            .IsRequired();

        builder.Property(p => p.TargetClassCReserve)
            .HasColumnName("target_class_c_reserve")
            .IsRequired();

        builder.Property(p => p.ProtectDeadStockFromUnnecessaryRelocation)
            .HasColumnName("protect_dead_stock")
            .IsRequired();

        builder.Property(p => p.MaxRelocationsPerBatch)
            .HasColumnName("max_relocations_per_batch")
            .IsRequired();

        builder.Property(p => p.MinLaborSavingsUsd)
            .HasColumnName("min_labor_savings_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.EstimatedRelocationLaborMinutes)
            .HasColumnName("estimated_relocation_labor_minutes")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.LaborHourlyRateUsd)
            .HasColumnName("labor_hourly_rate_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.DisruptionPenaltyWeight)
            .HasColumnName("disruption_penalty_weight")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightTravelDistance)
            .HasColumnName("weight_travel_distance")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightErgonomics)
            .HasColumnName("weight_ergonomics")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightPickDensity)
            .HasColumnName("weight_pick_density")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightAbcAffinity)
            .HasColumnName("weight_abc_affinity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightVelocityAffinity)
            .HasColumnName("weight_velocity_affinity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightCubeFill)
            .HasColumnName("weight_cube_fill")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightFinancialBenefit)
            .HasColumnName("weight_financial_benefit")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightOperationalUrgency)
            .HasColumnName("weight_operational_urgency")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightPlacementSuitability)
            .HasColumnName("weight_placement_suitability")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightDisruptionPenalty)
            .HasColumnName("weight_disruption_penalty")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.NegativeNetBenefitPolicy)
            .HasColumnName("negative_net_benefit_policy")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.UrgencyThresholdForNegativeBenefit)
            .HasColumnName("urgency_threshold_for_negative_benefit")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.TargetMonthlySavingsUsd)
            .HasColumnName("target_monthly_savings_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.MaxAcceptableRelocationCostUsd)
            .HasColumnName("max_acceptable_relocation_cost_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.MaxAcceptableDisruptionCostUsd)
            .HasColumnName("max_acceptable_disruption_cost_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.UseTotalRelocationCostForDisruptionPenalty)
            .HasColumnName("use_total_relocation_cost_for_disruption_penalty")
            .IsRequired();

        builder.Property(p => p.HandlingCostPerUnitUsd)
            .HasColumnName("handling_cost_per_unit_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.HeavyHandlingSurchargePerTierUsd)
            .HasColumnName("heavy_handling_surcharge_per_tier_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.BaseDisruptionCostUsd)
            .HasColumnName("base_disruption_cost_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.GoldenZoneOpportunityGainUsd)
            .HasColumnName("golden_zone_opportunity_gain_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.AmortizationMonths)
            .HasColumnName("amortization_months")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.MaxProjectionAgeDays)
            .HasColumnName("max_projection_age_days")
            .IsRequired();

        builder.Property(p => p.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(p => p.EffectiveFromUtc)
            .HasColumnName("effective_from_utc")
            .IsRequired();

        builder.Property(p => p.EffectiveToUtc)
            .HasColumnName("effective_to_utc")
            .IsRequired(false);

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasIndex(p => new { p.FacilityId, p.ClientId, p.IsActive });
    }
}
