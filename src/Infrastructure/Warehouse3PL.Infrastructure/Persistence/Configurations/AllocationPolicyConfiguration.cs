using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class AllocationPolicyConfiguration : IEntityTypeConfiguration<AllocationPolicy>
{
    public void Configure(EntityTypeBuilder<AllocationPolicy> builder)
    {
        builder.ToTable("allocation_policies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("policy_id")
            .ValueGeneratedNever();

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(p => p.ItemCategoryId)
            .HasColumnName("item_category_id");

        builder.Property(p => p.DefaultStrategy)
            .HasColumnName("default_strategy")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.AllowPartialAllocation)
            .HasColumnName("allow_partial_allocation")
            .IsRequired();

        builder.Property(p => p.AllowMixedLots)
            .HasColumnName("allow_mixed_lots")
            .IsRequired();

        builder.Property(p => p.MinDaysBeforeExpiry)
            .HasColumnName("min_days_before_expiry")
            .IsRequired();

        builder.Property(p => p.PreferFullHandlingUnits)
            .HasColumnName("prefer_full_handling_units")
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(p => p.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1u);

        // Auditing
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        // Unique index: Client + Facility + ItemCategoryId (allows only 1 policy per category or default per client/facility)
        builder.HasIndex(p => new { p.ClientId, p.FacilityId, p.ItemCategoryId })
            .IsUnique();

        // Relationships
        builder.HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Facility)
            .WithMany()
            .HasForeignKey(p => p.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ItemCategory)
            .WithMany()
            .HasForeignKey(p => p.ItemCategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

