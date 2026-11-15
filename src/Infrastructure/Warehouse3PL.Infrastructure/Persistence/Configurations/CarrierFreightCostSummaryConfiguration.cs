using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierFreightCostSummaryConfiguration : IEntityTypeConfiguration<CarrierFreightCostSummary>
{
    public void Configure(EntityTypeBuilder<CarrierFreightCostSummary> builder)
    {
        builder.ToTable("carrier_freight_cost_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("summary_id")
            .ValueGeneratedNever();

        builder.Property(s => s.CarrierCode).HasColumnName("carrier_code").HasMaxLength(50).IsRequired();
        builder.Property(s => s.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(s => s.ClientId).HasColumnName("client_id").IsRequired(false);

        builder.Property(s => s.DateUtc).HasColumnName("date_utc").IsRequired();
        builder.Property(s => s.TimeGrain).HasColumnName("time_grain").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(10).IsRequired();

        // Shipment Counts
        builder.Property(s => s.TotalShipmentsQuotedCount).HasColumnName("total_shipments_quoted_count").IsRequired();
        builder.Property(s => s.TotalShipmentsBilledCount).HasColumnName("total_shipments_billed_count").IsRequired();
        builder.Property(s => s.TotalShipmentsReconciledCount).HasColumnName("total_shipments_reconciled_count").IsRequired();

        // Quoted Baseline
        builder.Property(s => s.TotalQuotedBaseFreightUsd).HasColumnName("total_quoted_base_freight_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalQuotedAccessorialsUsd).HasColumnName("total_quoted_accessorials_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalQuotedAmountUsd).HasColumnName("total_quoted_amount_usd").HasPrecision(18, 2).IsRequired();

        // Estimated Freight
        builder.Property(s => s.TotalEstimatedAmountUsd).HasColumnName("total_estimated_amount_usd").HasPrecision(18, 2).IsRequired();

        // Authoritative Billed Freight
        builder.Property(s => s.TotalActualBaseFreightUsd).HasColumnName("total_actual_base_freight_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalActualAccessorialsUsd).HasColumnName("total_actual_accessorials_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalBilledAmountUsd).HasColumnName("total_billed_amount_usd").HasPrecision(18, 2).IsRequired();

        // Surcharge Breakdown
        builder.Property(s => s.FuelSurchargeUsd).HasColumnName("fuel_surcharge_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.ResidentialSurchargeUsd).HasColumnName("residential_surcharge_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.LiftgateFeeUsd).HasColumnName("liftgate_fee_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.AddressCorrectionFeeUsd).HasColumnName("address_correction_fee_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.OtherAdjustmentsUsd).HasColumnName("other_adjustments_usd").HasPrecision(18, 2).IsRequired();

        // Variance Metrics
        builder.Property(s => s.EstimatedVarianceUsd).HasColumnName("estimated_variance_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.BilledVarianceUsd).HasColumnName("billed_variance_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.ReconciledVarianceUsd).HasColumnName("reconciled_variance_usd").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.AccessorialDensityRatio).HasColumnName("accessorial_density_ratio").HasPrecision(18, 2).IsRequired(false);

        // Auditable Entity
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(s => new { s.CarrierCode, s.FacilityId, s.ClientId, s.DateUtc, s.TimeGrain, s.Currency })
            .HasDatabaseName("ix_carrier_freight_cost_summary_unique")
            .IsUnique();
    }
}
