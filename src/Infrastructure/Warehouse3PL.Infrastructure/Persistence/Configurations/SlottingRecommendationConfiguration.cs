using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class SlottingRecommendationConfiguration : IEntityTypeConfiguration<SlottingRecommendation>
{
    public void Configure(EntityTypeBuilder<SlottingRecommendation> builder)
    {
        builder.ToTable("slotting_recommendations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("recommendation_id")
            .ValueGeneratedNever();

        builder.Property(r => r.BatchId)
            .HasColumnName("batch_id")
            .IsRequired();

        builder.Property(r => r.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(r => r.ItemSku)
            .HasColumnName("item_sku")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(r => r.CurrentLocationId)
            .HasColumnName("current_location_id")
            .IsRequired();

        builder.Property(r => r.CurrentLocationCode)
            .HasColumnName("current_location_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.ProposedLocationId)
            .HasColumnName("proposed_location_id")
            .IsRequired();

        builder.Property(r => r.ProposedLocationCode)
            .HasColumnName("proposed_location_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.SourceZoneCode)
            .HasColumnName("source_zone_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.DestinationZoneCode)
            .HasColumnName("destination_zone_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.SkuVelocity)
            .HasColumnName("sku_velocity")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.AbcClass)
            .HasColumnName("abc_class")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(r => r.VelocityTrend)
            .HasColumnName("velocity_trend")
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(r => r.AbcAffinityScore)
            .HasColumnName("abc_affinity_score")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.VelocityAffinityScore)
            .HasColumnName("velocity_affinity_score")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.PickDensityScore)
            .HasColumnName("pick_density_score")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.CumulativeWeightConsumedKg)
            .HasColumnName("cumulative_weight_consumed_kg")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.CumulativeVolumeConsumedCm3)
            .HasColumnName("cumulative_volume_consumed_cm3")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.ReasonCode)
            .HasColumnName("reason_code")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.SuitabilityScore)
            .HasColumnName("suitability_score")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.ErgonomicScore)
            .HasColumnName("ergonomic_score")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.TravelDistanceDeltaMeters)
            .HasColumnName("travel_distance_delta_meters")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.EstimatedMoveLaborMinutes)
            .HasColumnName("estimated_move_labor_minutes")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.EstimatedMonthlyNetSavingsUsd)
            .HasColumnName("estimated_monthly_net_savings_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.GrossMonthlySavingsUsd)
            .HasColumnName("gross_monthly_savings_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.HandlingCostUsd)
            .HasColumnName("handling_cost_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.MovementCostUsd)
            .HasColumnName("movement_cost_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.DisruptionCostUsd)
            .HasColumnName("disruption_cost_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.TotalRelocationCostUsd)
            .HasColumnName("total_relocation_cost_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.UrgencyScore)
            .HasColumnName("urgency_score")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.UrgencyReason)
            .HasColumnName("urgency_reason")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(r => r.PriorityScore)
            .HasColumnName("priority_score")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.DisruptionLevel)
            .HasColumnName("disruption_level")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.IsOperationallyFeasible)
            .HasColumnName("is_operationally_feasible")
            .IsRequired();

        builder.Property(r => r.IsHardConstraintViolated)
            .HasColumnName("is_hard_constraint_violated")
            .IsRequired();

        builder.Property(r => r.ViolatedConstraintsSummary)
            .HasColumnName("violated_constraints_summary")
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(r => r.IsProjectionStaleOrMissing)
            .HasColumnName("is_projection_stale_or_missing")
            .IsRequired();

        builder.Property(r => r.ClassificationSource)
            .HasColumnName("classification_source")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.ProjectionCalculatedAtUtc)
            .HasColumnName("projection_calculated_at_utc")
            .IsRequired(false);

        builder.Property(r => r.ProjectionAgeDays)
            .HasColumnName("projection_age_days")
            .IsRequired(false);

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.HasOne(r => r.Item)
            .WithMany()
            .HasForeignKey(r => r.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CurrentLocation)
            .WithMany()
            .HasForeignKey(r => r.CurrentLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ProposedLocation)
            .WithMany()
            .HasForeignKey(r => r.ProposedLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.BatchId, r.ItemId });
        builder.HasIndex(r => new { r.BatchId, r.Status });
    }
}
