using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ShipmentFreightMarginRecordConfiguration : IEntityTypeConfiguration<ShipmentFreightMarginRecord>
{
    public void Configure(EntityTypeBuilder<ShipmentFreightMarginRecord> builder)
    {
        builder.ToTable("shipment_freight_margin_records");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("margin_record_id")
            .ValueGeneratedNever();

        builder.Property(m => m.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(m => m.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(m => m.ShipmentId)
            .HasColumnName("shipment_id")
            .IsRequired();

        builder.Property(m => m.CarrierCostRecordId)
            .HasColumnName("carrier_cost_record_id")
            .IsRequired();

        builder.Property(m => m.FreightAgreementId)
            .HasColumnName("freight_agreement_id");

        builder.Property(m => m.BillableActivityId)
            .HasColumnName("billable_activity_id");

        builder.Property(m => m.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100);

        builder.Property(m => m.PricingModelApplied)
            .HasColumnName("pricing_model_applied")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.PackageCount)
            .HasColumnName("package_count")
            .IsRequired();

        // Buy-side
        builder.Property(m => m.CarrierBaseCost).HasColumnName("carrier_base_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.CarrierFuelSurcharge).HasColumnName("carrier_fuel_surcharge").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.CarrierInsuranceCost).HasColumnName("carrier_insurance_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.CarrierAccessorialCost).HasColumnName("carrier_accessorial_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.TotalCarrierCost).HasColumnName("total_carrier_cost").HasPrecision(18, 4).IsRequired();

        // Sell-side
        builder.Property(m => m.ClientBaseFreight).HasColumnName("client_base_freight").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.ClientFuelCharge).HasColumnName("client_fuel_charge").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.ClientInsuranceCharge).HasColumnName("client_insurance_charge").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.ClientAccessorialCharge).HasColumnName("client_accessorial_charge").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.ClientMarkupAmount).HasColumnName("client_markup_amount").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.ClientHandlingFee).HasColumnName("client_handling_fee").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.TotalClientFreight).HasColumnName("total_client_freight").HasPrecision(18, 4).IsRequired();

        // Margin
        builder.Property(m => m.GrossMarginAmount).HasColumnName("gross_margin_amount").HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.MarkupPercentage).HasColumnName("markup_percentage").HasPrecision(18, 2).IsRequired();
        builder.Property(m => m.MarginPercentage).HasColumnName("margin_percentage").HasPrecision(18, 2).IsRequired();

        builder.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(m => m.RatedAtUtc).HasColumnName("rated_at_utc").IsRequired();
        builder.Property(m => m.IsReconciled).HasColumnName("is_reconciled").IsRequired();
        builder.Property(m => m.AdjustmentCount).HasColumnName("adjustment_count").IsRequired();

        // Net Adjusted
        builder.Property(m => m.NetAdjustedCarrierCost).HasColumnName("net_adjusted_carrier_cost").HasPrecision(18, 4);
        builder.Property(m => m.NetAdjustedClientFreight).HasColumnName("net_adjusted_client_freight").HasPrecision(18, 4);
        builder.Property(m => m.NetAdjustedMargin).HasColumnName("net_adjusted_margin").HasPrecision(18, 4);

        builder.Property(m => m.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();

        // Auditing
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.CreatedBy).HasColumnName("created_by");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.Property(m => m.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(m => new { m.ClientId, m.IdempotencyKey })
            .HasDatabaseName("ix_shipment_freight_margin_records_idempotency")
            .IsUnique();

        builder.HasIndex(m => m.ShipmentId)
            .HasDatabaseName("ix_shipment_freight_margin_records_shipment")
            .IsUnique();

        builder.HasIndex(m => new { m.ClientId, m.RatedAtUtc })
            .HasDatabaseName("ix_shipment_freight_margin_records_client_rated");

        // Relationships
        builder.HasOne(m => m.Client).WithMany().HasForeignKey(m => m.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Facility).WithMany().HasForeignKey(m => m.FacilityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Shipment).WithMany().HasForeignKey(m => m.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.CarrierCostRecord).WithMany().HasForeignKey(m => m.CarrierCostRecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.FreightAgreement).WithMany().HasForeignKey(m => m.FreightAgreementId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(m => m.BillableActivity).WithMany().HasForeignKey(m => m.BillableActivityId).OnDelete(DeleteBehavior.SetNull);
    }
}

