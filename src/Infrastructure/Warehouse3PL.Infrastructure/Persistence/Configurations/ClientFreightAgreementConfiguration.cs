using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ClientFreightAgreementConfiguration : IEntityTypeConfiguration<ClientFreightAgreement>
{
    public void Configure(EntityTypeBuilder<ClientFreightAgreement> builder)
    {
        builder.ToTable("client_freight_agreements");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("freight_agreement_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(a => a.AgreementCode)
            .HasColumnName("agreement_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(a => a.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50);

        builder.Property(a => a.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(a => a.PricingModel)
            .HasColumnName("pricing_model")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.MarkupPercentage)
            .HasColumnName("markup_percentage")
            .HasPrecision(18, 4);

        builder.Property(a => a.FlatFeePerShipment)
            .HasColumnName("flat_fee_per_shipment")
            .HasPrecision(18, 4);

        builder.Property(a => a.FlatFeePerPackage)
            .HasColumnName("flat_fee_per_package")
            .HasPrecision(18, 4);

        builder.Property(a => a.FixedFreightRate)
            .HasColumnName("fixed_freight_rate")
            .HasPrecision(18, 4);

        builder.Property(a => a.WeightRoundingIncrementKg)
            .HasColumnName("weight_rounding_increment_kg")
            .HasPrecision(18, 4);

        builder.Property(a => a.MinimumMarkupAmount)
            .HasColumnName("minimum_markup_amount")
            .HasPrecision(18, 4);

        builder.Property(a => a.MaximumMarkupAmount)
            .HasColumnName("maximum_markup_amount")
            .HasPrecision(18, 4);

        builder.Property(a => a.DefaultAccessorialTreatment)
            .HasColumnName("default_accessorial_treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

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

        builder.Property(a => a.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        // Auditing
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(a => new { a.ClientId, a.AgreementCode })
            .HasDatabaseName("ix_client_freight_agreements_code")
            .IsUnique();

        builder.HasIndex(a => new { a.ClientId, a.FacilityId, a.CarrierCode, a.IsActive })
            .HasDatabaseName("ix_client_freight_agreements_lookup");

        // Relationships
        builder.HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Facility)
            .WithMany()
            .HasForeignKey(a => a.FacilityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(a => a.AccessorialRules)
            .WithOne(r => r.ClientFreightAgreement)
            .HasForeignKey(r => r.ClientFreightAgreementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

