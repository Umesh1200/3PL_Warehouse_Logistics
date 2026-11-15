using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class AbcClassificationPolicyConfiguration : IEntityTypeConfiguration<AbcClassificationPolicy>
{
    public void Configure(EntityTypeBuilder<AbcClassificationPolicy> builder)
    {
        builder.ToTable("abc_classification_policies");

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

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(p => p.Criteria)
            .HasColumnName("criteria")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.ClassACumulativeThresholdPercent)
            .HasColumnName("class_a_cumulative_threshold_percent")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.ClassBCumulativeThresholdPercent)
            .HasColumnName("class_b_cumulative_threshold_percent")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.ObservationWindowDays)
            .HasColumnName("observation_window_days")
            .IsRequired();

        builder.Property(p => p.ObservationWindow)
            .HasColumnName("observation_window")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.CostValuationBasis)
            .HasColumnName("cost_valuation_basis")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.WeightAcv)
            .HasColumnName("weight_acv")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightPickFrequency)
            .HasColumnName("weight_pick_frequency")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightUnitVelocity)
            .HasColumnName("weight_unit_velocity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.WeightOrderLines)
            .HasColumnName("weight_order_lines")
            .HasPrecision(18, 4)
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
            .HasDatabaseName("ix_abc_classification_policies_facility_client_active");
    }
}

