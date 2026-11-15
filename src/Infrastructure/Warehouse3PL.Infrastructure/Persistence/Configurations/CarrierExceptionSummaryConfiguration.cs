using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierExceptionSummaryConfiguration : IEntityTypeConfiguration<CarrierExceptionSummary>
{
    public void Configure(EntityTypeBuilder<CarrierExceptionSummary> builder)
    {
        builder.ToTable("carrier_exception_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("exception_summary_id")
            .ValueGeneratedNever();

        builder.Property(s => s.CarrierCode).HasColumnName("carrier_code").HasMaxLength(50).IsRequired();
        builder.Property(s => s.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(s => s.ClientId).HasColumnName("client_id").IsRequired(false);

        builder.Property(s => s.DateUtc).HasColumnName("date_utc").IsRequired();
        builder.Property(s => s.TimeGrain).HasColumnName("time_grain").HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(s => s.ExceptionCategory).HasColumnName("exception_category").HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(s => s.TotalExceptionsCount).HasColumnName("total_exceptions_count").IsRequired();
        builder.Property(s => s.ResolvedDeliveredCount).HasColumnName("resolved_delivered_count").IsRequired();
        builder.Property(s => s.PendingResolutionCount).HasColumnName("pending_resolution_count").IsRequired();
        builder.Property(s => s.ReturnedToOriginCount).HasColumnName("returned_to_origin_count").IsRequired();
        builder.Property(s => s.LostClaimRecommendedCount).HasColumnName("lost_claim_recommended_count").IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(s => new { s.CarrierCode, s.FacilityId, s.ClientId, s.ExceptionCategory, s.DateUtc, s.TimeGrain })
            .HasDatabaseName("ix_carrier_exception_carrier_fac_client_cat_date_grain")
            .IsUnique();
    }
}
