using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierRateAgreementConfiguration : IEntityTypeConfiguration<CarrierRateAgreement>
{
    public void Configure(EntityTypeBuilder<CarrierRateAgreement> builder)
    {
        builder.ToTable("carrier_rate_agreements");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("carrier_rate_agreement_id")
            .ValueGeneratedNever();

        builder.Property(a => a.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(a => a.AgreementCode)
            .HasColumnName("agreement_code")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id");

        builder.Property(a => a.ZoneOrLane)
            .HasColumnName("zone_or_lane")
            .HasMaxLength(100);

        builder.Property(a => a.DestinationCountryCode).HasMaxLength(3);
        builder.Property(a => a.DestinationStateOrProvince).HasMaxLength(100);
        builder.Property(a => a.DestinationPostalCodePrefix).HasMaxLength(20);
        builder.Property(a => a.DestinationFacilityCode).HasMaxLength(50);

        builder.Property(a => a.RateModel)
            .HasColumnName("rate_model")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.TierModel)
            .HasColumnName("tier_model")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.BaseRate)
            .HasColumnName("base_rate")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.UnitOfMeasure)
            .HasColumnName("unit_of_measure")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.MinimumCharge)
            .HasColumnName("minimum_charge")
            .HasPrecision(18, 4);

        builder.Property(a => a.DimensionalWeightDivisor)
            .HasColumnName("dimensional_weight_divisor")
            .HasPrecision(18, 4);

        builder.Property(a => a.WeightRoundingIncrementKg)
            .HasColumnName("weight_rounding_increment_kg")
            .HasPrecision(18, 4);

        builder.Ignore(a => a.WeightRoundingIncrement);

        builder.Property(a => a.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(a => a.EffectiveFromUtc)
            .HasColumnName("effective_from_utc")
            .IsRequired();

        builder.Property(a => a.EffectiveToUtc)
            .HasColumnName("effective_to_utc");

        builder.Property(a => a.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(a => a.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(a => a.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(a => a.UpdatedBy)
            .HasColumnName("updated_by");

        // Soft Delete
        builder.Property(a => a.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired();

        builder.Property(a => a.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(a => a.DeletedBy)
            .HasColumnName("deleted_by");

        // Relational Foreign Keys (Finding 5)
        builder.HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Facility)
            .WithMany()
            .HasForeignKey(a => a.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Tiers)
            .WithOne(t => t.CarrierRateAgreement)
            .HasForeignKey(t => t.CarrierRateAgreementId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(a => new { a.CarrierCode, a.IsActive, a.EffectiveFromUtc })
            .HasDatabaseName("ix_carrier_rate_agreements_carrier_active");

        builder.HasIndex(a => new { a.ClientId, a.CarrierCode, a.IsActive })
            .HasDatabaseName("ix_carrier_rate_agreements_client_lookup");

        builder.HasIndex(a => new { a.FacilityId, a.CarrierCode, a.IsActive })
            .HasDatabaseName("ix_carrier_rate_agreements_facility_lookup");
    }
}
