using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierSlaComplianceRecordConfiguration : IEntityTypeConfiguration<CarrierSlaComplianceRecord>
{
    public void Configure(EntityTypeBuilder<CarrierSlaComplianceRecord> builder)
    {
        builder.ToTable("carrier_sla_compliance_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("compliance_record_id")
            .ValueGeneratedNever();

        builder.Property(r => r.ShipmentId)
            .HasColumnName("shipment_id")
            .IsRequired();

        builder.Property(r => r.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(r => r.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(r => r.OriginRegion)
            .HasColumnName("origin_region")
            .HasMaxLength(50);

        builder.Property(r => r.DestinationCountry)
            .HasColumnName("destination_country")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(r => r.CountryNormalizationStatus)
            .HasColumnName("country_normalization_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CountryNormalizationStatus.PendingNormalization)
            .IsRequired();

        builder.Property(r => r.DestinationStateOrRegion)
            .HasColumnName("destination_state_or_region")
            .HasMaxLength(50);

        builder.Property(r => r.DestinationPostalCodePrefix)
            .HasColumnName("destination_postal_prefix")
            .HasMaxLength(20);

        builder.Property(r => r.DispatchedAtUtc)
            .HasColumnName("dispatched_at_utc")
            .IsRequired();

        builder.Property(r => r.EstimatedDeliveryDateUtc)
            .HasColumnName("estimated_delivery_date_utc")
            .IsRequired(false);

        builder.Property(r => r.DeliveredAtUtc)
            .HasColumnName("delivered_at_utc")
            .IsRequired(false);

        builder.Property(r => r.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(r => r.IsFirstAttemptSuccess)
            .HasColumnName("is_first_attempt_success")
            .IsRequired();

        builder.Property(r => r.FirstAttemptAtUtc)
            .HasColumnName("first_attempt_at_utc")
            .IsRequired(false);

        builder.Property(r => r.ContractualTransitDays)
            .HasColumnName("contractual_transit_days")
            .IsRequired();

        builder.Property(r => r.ActualTransitDays)
            .HasColumnName("actual_transit_days")
            .HasPrecision(18, 2)
            .IsRequired(false);

        builder.Property(r => r.TransitTimeVarianceDays)
            .HasColumnName("transit_time_variance_days")
            .HasPrecision(18, 2)
            .IsRequired(false);

        builder.Property(r => r.IsOnTime)
            .HasColumnName("is_on_time")
            .IsRequired(false);

        builder.Property(r => r.HasDeliveryException)
            .HasColumnName("has_delivery_exception")
            .IsRequired();

        builder.Property(r => r.QualifyingExceptionsCount)
            .HasColumnName("qualifying_exceptions_count")
            .IsRequired();

        builder.Property(r => r.SlaPolicyId)
            .HasColumnName("sla_policy_id")
            .IsRequired(false);

        builder.Property(r => r.SlaPolicyVersion)
            .HasColumnName("sla_policy_version")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.EvaluationStatus)
            .HasColumnName("evaluation_status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.DataQualityNotes)
            .HasColumnName("data_quality_notes")
            .HasMaxLength(500);

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(r => r.ShipmentId).IsUnique();
        builder.HasIndex(r => r.TrackingNumber);
        builder.HasIndex(r => new { r.FacilityId, r.ClientId, r.DispatchedAtUtc });
        builder.HasIndex(r => new { r.CarrierCode, r.CarrierServiceLevel, r.DispatchedAtUtc });
    }
}
