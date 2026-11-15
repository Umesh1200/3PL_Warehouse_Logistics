using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class SlottingRecommendationBatchConfiguration : IEntityTypeConfiguration<SlottingRecommendationBatch>
{
    public void Configure(EntityTypeBuilder<SlottingRecommendationBatch> builder)
    {
        builder.ToTable("slotting_recommendation_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasColumnName("batch_id")
            .ValueGeneratedNever();

        builder.Property(b => b.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(b => b.ClientId)
            .HasColumnName("client_id")
            .IsRequired(false);

        builder.Property(b => b.PolicyId)
            .HasColumnName("policy_id")
            .IsRequired();

        builder.Property(b => b.PolicyVersion)
            .HasColumnName("policy_version")
            .IsRequired();

        builder.Property(b => b.PolicySnapshotJson)
            .HasColumnName("policy_snapshot_json")
            .IsRequired(false);

        builder.Property(b => b.BatchNumber)
            .HasColumnName("batch_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(b => b.SnapshotDateUtc)
            .HasColumnName("snapshot_date_utc")
            .IsRequired();

        builder.Property(b => b.TotalProposedMoves)
            .HasColumnName("total_proposed_moves")
            .IsRequired();

        builder.Property(b => b.ApprovedMovesCount)
            .HasColumnName("approved_moves_count")
            .IsRequired();

        builder.Property(b => b.RejectedMovesCount)
            .HasColumnName("rejected_moves_count")
            .IsRequired();

        builder.Property(b => b.ExecutedMovesCount)
            .HasColumnName("executed_moves_count")
            .IsRequired();

        builder.Property(b => b.EstimatedLaborMinutesTotal)
            .HasColumnName("estimated_labor_minutes_total")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(b => b.EstimatedTravelDistanceSavedMetersTotal)
            .HasColumnName("estimated_travel_distance_saved_meters_total")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(b => b.EstimatedMonthlyNetSavingsUsd)
            .HasColumnName("estimated_monthly_net_savings_usd")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(b => b.AverageSuitabilityScore)
            .HasColumnName("average_suitability_score")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(b => b.ApprovedBy)
            .HasColumnName("approved_by")
            .IsRequired(false);

        builder.Property(b => b.ApprovedAtUtc)
            .HasColumnName("approved_at_utc")
            .IsRequired(false);

        builder.Property(b => b.ExecutionStartedAtUtc)
            .HasColumnName("execution_started_at_utc")
            .IsRequired(false);

        builder.Property(b => b.ExecutionCompletedAtUtc)
            .HasColumnName("execution_completed_at_utc")
            .IsRequired(false);

        builder.Property(b => b.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.HasOne(b => b.Policy)
            .WithMany()
            .HasForeignKey(b => b.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Recommendations)
            .WithOne(r => r.Batch)
            .HasForeignKey(r => r.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => new { b.FacilityId, b.BatchNumber }).IsUnique();
        builder.HasIndex(b => new { b.FacilityId, b.Status });
    }
}
