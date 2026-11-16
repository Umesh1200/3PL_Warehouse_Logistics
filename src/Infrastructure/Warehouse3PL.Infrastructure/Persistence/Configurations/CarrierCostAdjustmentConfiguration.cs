using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierCostAdjustmentConfiguration : IEntityTypeConfiguration<CarrierCostAdjustment>
{
    public void Configure(EntityTypeBuilder<CarrierCostAdjustment> builder)
    {
        builder.ToTable("carrier_cost_adjustments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("adjustment_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.CarrierCostRecordId)
            .HasColumnName("carrier_cost_record_id")
            .IsRequired();

        builder.Property(a => a.ShipmentFreightMarginRecordId)
            .HasColumnName("shipment_freight_margin_record_id");

        builder.Property(a => a.AdjustmentType)
            .HasColumnName("adjustment_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.CarrierInvoiceNumber)
            .HasColumnName("carrier_invoice_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.OriginalCarrierCost)
            .HasColumnName("original_carrier_cost")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.AdjustedCarrierCost)
            .HasColumnName("adjusted_carrier_cost")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.CarrierCostDelta)
            .HasColumnName("carrier_cost_delta")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.ClientFreightDelta)
            .HasColumnName("client_freight_delta")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.BilledWeightKg)
            .HasColumnName("billed_weight_kg")
            .HasPrecision(18, 4);

        builder.Property(a => a.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired(false);

        builder.Property(a => a.Reason)
            .HasColumnName("reason")
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(a => a.AdjustedAtUtc)
            .HasColumnName("adjusted_at_utc")
            .IsRequired();

        builder.Property(a => a.BillableActivityAdjustmentId)
            .HasColumnName("billable_activity_adjustment_id");

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
            .HasDatabaseName("ix_carrier_cost_adjustments_idempotency")
            .IsUnique();

        builder.HasIndex(a => a.CarrierCostRecordId)
            .HasDatabaseName("ix_carrier_cost_adjustments_cost_record");

        builder.HasIndex(a => a.CarrierInvoiceNumber)
            .HasDatabaseName("ix_carrier_cost_adjustments_invoice");

        // Relationships
        builder.HasOne(a => a.Client).WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Facility).WithMany().HasForeignKey(a => a.FacilityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.CarrierCostRecord).WithMany(c => c.Adjustments).HasForeignKey(a => a.CarrierCostRecordId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.ShipmentFreightMarginRecord).WithMany().HasForeignKey(a => a.ShipmentFreightMarginRecordId).OnDelete(DeleteBehavior.SetNull);
    }
}

