using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierAccessorialChargeConfiguration : IEntityTypeConfiguration<CarrierAccessorialCharge>
{
    public void Configure(EntityTypeBuilder<CarrierAccessorialCharge> builder)
    {
        builder.ToTable("carrier_accessorial_charges");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("accessorial_charge_id")
            .ValueGeneratedNever();

        builder.Property(a => a.CarrierCostRecordId)
            .HasColumnName("carrier_cost_record_id")
            .IsRequired();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.ShipmentId)
            .HasColumnName("shipment_id")
            .IsRequired();

        builder.Property(a => a.PackageId)
            .HasColumnName("package_id");

        builder.Property(a => a.AccessorialType)
            .HasColumnName("accessorial_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.CarrierAccessorialCode)
            .HasColumnName("carrier_accessorial_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Description)
            .HasColumnName("description")
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(a => a.CarrierAmount)
            .HasColumnName("carrier_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.ClientAmount)
            .HasColumnName("client_amount")
            .HasPrecision(18, 4);

        builder.Property(a => a.BillableTreatment)
            .HasColumnName("billable_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(a => a.AssessedAtUtc)
            .HasColumnName("assessed_at_utc")
            .IsRequired();

        builder.Property(a => a.SourceDocumentReference)
            .HasColumnName("source_document_reference")
            .HasMaxLength(100);

        builder.Property(a => a.IsReconciled)
            .HasColumnName("is_reconciled")
            .IsRequired();

        builder.Property(a => a.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        // Auditing
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(a => new { a.ClientId, a.IdempotencyKey })
            .HasDatabaseName("ix_carrier_accessorial_charges_client_idempotency")
            .IsUnique();

        builder.HasIndex(a => a.CarrierCostRecordId)
            .HasDatabaseName("ix_carrier_accessorial_charges_cost_record");

        builder.HasIndex(a => a.ShipmentId)
            .HasDatabaseName("ix_carrier_accessorial_charges_shipment");

        // Relationships
        builder.HasOne(a => a.CarrierCostRecord)
            .WithMany(c => c.AccessorialCharges)
            .HasForeignKey(a => a.CarrierCostRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Facility)
            .WithMany()
            .HasForeignKey(a => a.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Shipment)
            .WithMany()
            .HasForeignKey(a => a.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Package)
            .WithMany()
            .HasForeignKey(a => a.PackageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

