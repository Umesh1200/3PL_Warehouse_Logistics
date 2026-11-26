using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierContractualSlaPolicyConfiguration : IEntityTypeConfiguration<CarrierContractualSlaPolicy>
{
    public void Configure(EntityTypeBuilder<CarrierContractualSlaPolicy> builder)
    {
        builder.ToTable("carrier_contractual_sla_policies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("policy_id")
            .ValueGeneratedNever();

        builder.Property(p => p.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired(false);

        builder.Property(p => p.OriginFacilityId)
            .HasColumnName("origin_facility_id")
            .IsRequired(false);

        builder.Property(p => p.OriginRegion)
            .HasColumnName("origin_region")
            .HasMaxLength(50);

        builder.Property(p => p.DestinationCountry)
            .HasColumnName("destination_country")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(p => p.CountryNormalizationStatus)
            .HasColumnName("country_normalization_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CountryNormalizationStatus.PendingNormalization)
            .IsRequired();

        builder.Property(p => p.DestinationStateOrRegion)
            .HasColumnName("destination_state_or_region")
            .HasMaxLength(50);

        builder.Property(p => p.DestinationPostalCodePrefix)
            .HasColumnName("destination_postal_prefix")
            .HasMaxLength(20);

        builder.Property(p => p.TransitCommitmentDays)
            .HasColumnName("transit_commitment_days")
            .IsRequired();

        builder.Property(p => p.CalculationType)
            .HasColumnName("calculation_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.DailyCutoffTime)
            .HasColumnName("daily_cutoff_time")
            .IsRequired();

        builder.Property(p => p.CutoffTimeZone)
            .HasColumnName("cutoff_time_zone")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.EffectiveFromUtc)
            .HasColumnName("effective_from_utc")
            .IsRequired();

        builder.Property(p => p.EffectiveToUtc)
            .HasColumnName("effective_to_utc")
            .IsRequired(false);

        builder.Property(p => p.SlaPolicyVersion)
            .HasColumnName("sla_policy_version")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(p => p.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(p => new { p.CarrierCode, p.CarrierServiceLevel, p.OriginFacilityId, p.ClientId, p.EffectiveFromUtc, p.SlaPolicyVersion })
            .HasDatabaseName("ix_carrier_sla_policy_carrier_svc_origin_client_eff_version");
    }
}
