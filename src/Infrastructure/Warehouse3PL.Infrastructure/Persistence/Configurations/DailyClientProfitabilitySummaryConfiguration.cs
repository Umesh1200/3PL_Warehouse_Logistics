using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyClientProfitabilitySummaryConfiguration : IEntityTypeConfiguration<DailyClientProfitabilitySummary>
{
    public void Configure(EntityTypeBuilder<DailyClientProfitabilitySummary> builder)
    {
        builder.ToTable("daily_client_profitability_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("summary_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(s => s.DateUtc)
            .HasColumnName("date_utc")
            .IsRequired();

        builder.Property(s => s.TimeGrain)
            .HasColumnName("time_grain")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        // Revenue Breakdown
        builder.Property(s => s.BilledStorageRevenue).HasColumnName("billed_storage_revenue").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.BilledHandlingRevenue).HasColumnName("billed_handling_revenue").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.BilledFreightRevenue).HasColumnName("billed_freight_revenue").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.BilledVasRevenue).HasColumnName("billed_vas_revenue").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalBilledRevenue).HasColumnName("total_billed_revenue").HasPrecision(18, 2).IsRequired();

        // Cost & Margins
        builder.Property(s => s.CarrierFreightCost).HasColumnName("carrier_freight_cost").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.NetFreightMargin).HasColumnName("net_freight_margin").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.FreightMarginPercent).HasColumnName("freight_margin_percent").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.DirectLaborEstimatedCost).HasColumnName("direct_labor_estimated_cost").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.NetCommercialMargin).HasColumnName("net_commercial_margin").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.CommercialMarginPercent).HasColumnName("commercial_margin_percent").HasPrecision(18, 2).IsRequired();

        // Billing & AR Realization
        builder.Property(s => s.InvoicesIssuedCount).HasColumnName("invoices_issued_count").IsRequired();
        builder.Property(s => s.TotalInvoicedAmount).HasColumnName("total_invoiced_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalTaxAmount).HasColumnName("total_tax_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.PaymentsCollectedAmount).HasColumnName("payments_collected_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.CreditsAppliedAmount).HasColumnName("credits_applied_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.OutstandingArBalance).HasColumnName("outstanding_ar_balance").HasPrecision(18, 2).IsRequired();

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee
        builder.HasIndex(s => new { s.ClientId, s.FacilityId, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_daily_client_profitability_client_fac_date_grain")
            .IsUnique();

        builder.HasIndex(s => new { s.ClientId, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_daily_client_profitability_client_date_grain");
    }
}

