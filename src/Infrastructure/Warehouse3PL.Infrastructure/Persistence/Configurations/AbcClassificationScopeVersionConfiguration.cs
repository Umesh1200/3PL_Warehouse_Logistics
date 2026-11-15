using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class AbcClassificationScopeVersionConfiguration : IEntityTypeConfiguration<AbcClassificationScopeVersion>
{
    public void Configure(EntityTypeBuilder<AbcClassificationScopeVersion> builder)
    {
        builder.ToTable("abc_classification_scope_versions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("scope_version_id")
            .ValueGeneratedNever();

        builder.Property(v => v.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(v => v.ClientId)
            .HasColumnName("client_id")
            .IsRequired(false);

        builder.Property(v => v.Criteria)
            .HasColumnName("criteria")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(v => v.ObservationWindow)
            .HasColumnName("observation_window")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(v => v.PolicyId)
            .HasColumnName("policy_id")
            .IsRequired(false);

        builder.Property(v => v.LatestVersion)
            .HasColumnName("latest_version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(v => v.LastRunId)
            .HasColumnName("last_run_id")
            .IsRequired(false);

        builder.Property(v => v.LastAllocatedAtUtc)
            .HasColumnName("last_allocated_at_utc")
            .IsRequired();

        // Auditing
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(v => v.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(v => v.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        // Unique constraint on scope
        builder.HasIndex(v => new { v.FacilityId, v.ClientId, v.Criteria, v.ObservationWindow })
            .HasDatabaseName("ix_abc_scope_versions_facility_client_criteria_window")
            .IsUnique();
    }
}
