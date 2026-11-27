using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LocationHierarchyRuleConfiguration : IEntityTypeConfiguration<LocationHierarchyRule>
{
    public void Configure(EntityTypeBuilder<LocationHierarchyRule> builder)
    {
        builder.ToTable("location_hierarchy_rules", t =>
        {
            t.HasCheckConstraint("ck_location_hierarchy_rules_max_depth", "max_depth >= 1 AND max_depth <= 10");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("rule_id").ValueGeneratedNever();
        builder.Property(r => r.FacilityId).HasColumnName("facility_id");
        builder.Property(r => r.ParentLocationType).HasColumnName("parent_location_type").HasConversion<string>().HasMaxLength(50);
        builder.Property(r => r.ChildLocationType).HasColumnName("child_location_type").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(r => r.MaxDepth).HasColumnName("max_depth").HasDefaultValue(6).IsRequired();
        builder.Property(r => r.IsStorableLeaf).HasColumnName("is_storable_leaf").HasDefaultValue(false).IsRequired();
        builder.Property(r => r.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(r => r.ConfigRevision).HasColumnName("config_revision").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        builder.HasOne(r => r.Facility)
            .WithMany()
            .HasForeignKey(r => r.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deterministic uniqueness for global vs facility & root (nullable parent) vs transition rules (A02)
        builder.HasIndex(r => r.ChildLocationType)
            .HasDatabaseName("uq_loc_hier_rules_global_root")
            .IsUnique()
            .HasFilter("facility_id IS NULL AND parent_location_type IS NULL");

        builder.HasIndex(r => new { r.ParentLocationType, r.ChildLocationType })
            .HasDatabaseName("uq_loc_hier_rules_global_transition")
            .IsUnique()
            .HasFilter("facility_id IS NULL AND parent_location_type IS NOT NULL");

        builder.HasIndex(r => new { r.FacilityId, r.ChildLocationType })
            .HasDatabaseName("uq_loc_hier_rules_facility_root")
            .IsUnique()
            .HasFilter("facility_id IS NOT NULL AND parent_location_type IS NULL");

        builder.HasIndex(r => new { r.FacilityId, r.ParentLocationType, r.ChildLocationType })
            .HasDatabaseName("uq_loc_hier_rules_facility_transition")
            .IsUnique()
            .HasFilter("facility_id IS NOT NULL AND parent_location_type IS NOT NULL");
    }
}
