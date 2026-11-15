using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class StorageBillingPolicyConfiguration : IEntityTypeConfiguration<StorageBillingPolicy>
{
    public void Configure(EntityTypeBuilder<StorageBillingPolicy> builder)
    {
        builder.ToTable("storage_billing_policies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("policy_id")
            .ValueGeneratedNever();

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(p => p.PolicyName)
            .HasColumnName("policy_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(p => p.AvailableTreatment)
            .HasColumnName("available_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.AllocatedTreatment)
            .HasColumnName("allocated_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.PickedTreatment)
            .HasColumnName("picked_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.DamagedTreatment)
            .HasColumnName("damaged_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.QuarantinedTreatment)
            .HasColumnName("quarantined_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.OnHoldTreatment)
            .HasColumnName("on_hold_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.InTransitTreatment)
            .HasColumnName("in_transit_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.ReturnPendingInspectionTreatment)
            .HasColumnName("return_pending_inspection_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.FallbackUnitCbm)
            .HasColumnName("fallback_unit_cbm")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.FallbackUnitsPerPallet)
            .HasColumnName("fallback_units_per_pallet")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.FallbackUnitsPerCarton)
            .HasColumnName("fallback_units_per_carton")
            .HasPrecision(18, 4)
            .IsRequired();

        // Auditing
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(p => new { p.ClientId, p.FacilityId })
            .HasDatabaseName("ix_storage_billing_policies_client_facility")
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
    }
}

