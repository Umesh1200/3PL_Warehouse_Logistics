using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierCostRecordConfiguration : IEntityTypeConfiguration<CarrierCostRecord>
{
    public void Configure(EntityTypeBuilder<CarrierCostRecord> builder)
    {
        builder.ToTable("carrier_cost_records");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("cost_record_id")
            .ValueGeneratedNever();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(c => c.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(c => c.ShipmentId)
            .HasColumnName("shipment_id")
            .IsRequired();

        builder.Property(c => c.PackageId)
            .HasColumnName("package_id");

        builder.Property(c => c.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100);

        builder.Property(c => c.CarrierInvoiceNumber)
            .HasColumnName("carrier_invoice_number")
            .HasMaxLength(100);

        builder.Property(c => c.BaseTransportationCost)
            .HasColumnName("base_transportation_cost")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(c => c.FuelSurcharge)
            .HasColumnName("fuel_surcharge")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(c => c.InsuranceCost)
            .HasColumnName("insurance_cost")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(c => c.AccessorialCost)
            .HasColumnName("accessorial_cost")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(c => c.TotalCarrierCost)
            .HasColumnName("total_carrier_cost")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(c => c.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.CostAssessedAtUtc)
            .HasColumnName("cost_assessed_at_utc")
            .IsRequired();

        builder.Property(c => c.InvoicedAtUtc)
            .HasColumnName("invoiced_at_utc");

        builder.Property(c => c.ReconciledAtUtc)
            .HasColumnName("reconciled_at_utc");

        builder.Property(c => c.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(c => c.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        // Auditing
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(c => new { c.ClientId, c.IdempotencyKey })
            .HasDatabaseName("ix_carrier_cost_records_client_idempotency")
            .IsUnique();

        builder.HasIndex(c => new { c.ClientId, c.ShipmentId })
            .HasDatabaseName("ix_carrier_cost_records_shipment");

        builder.HasIndex(c => c.CarrierInvoiceNumber)
            .HasDatabaseName("ix_carrier_cost_records_invoice");

        // Relationships
        builder.HasOne(c => c.Client)
            .WithMany()
            .HasForeignKey(c => c.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Facility)
            .WithMany()
            .HasForeignKey(c => c.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Shipment)
            .WithMany()
            .HasForeignKey(c => c.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Package)
            .WithMany()
            .HasForeignKey(c => c.PackageId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.AccessorialCharges)
            .WithOne(a => a.CarrierCostRecord)
            .HasForeignKey(a => a.CarrierCostRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Adjustments)
            .WithOne(a => a.CarrierCostRecord)
            .HasForeignKey(a => a.CarrierCostRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

