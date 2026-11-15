using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PutawayStrategyRuleConfiguration : IEntityTypeConfiguration<PutawayStrategyRule>
{
    public void Configure(EntityTypeBuilder<PutawayStrategyRule> builder)
    {
        builder.ToTable("putaway_strategy_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("rule_id")
            .ValueGeneratedNever();

        builder.Property(r => r.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(r => r.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(r => r.Sequence)
            .HasColumnName("sequence")
            .IsRequired();

        builder.Property(r => r.RuleName)
            .HasColumnName("rule_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(r => r.TempClassRequirement)
            .HasColumnName("temp_class_requirement")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.TargetVelocity)
            .HasColumnName("target_velocity")
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(r => r.PreferConsolidation)
            .HasColumnName("prefer_consolidation")
            .IsRequired();

        builder.Property(r => r.AllowDynamicOverflow)
            .HasColumnName("allow_dynamic_overflow")
            .IsRequired();

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(r => r.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // Auditing
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        // Soft delete
        builder.Property(r => r.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");
        builder.Property(r => r.DeletedBy).HasColumnName("deleted_by");
        builder.HasQueryFilter(r => !r.IsDeleted);

        // Indexes
        builder.HasIndex(r => new { r.FacilityId, r.Sequence });
        builder.HasIndex(r => new { r.ClientId, r.FacilityId });

        // Relationships
        builder.HasOne(r => r.Client)
            .WithMany()
            .HasForeignKey(r => r.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Facility)
            .WithMany()
            .HasForeignKey(r => r.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

