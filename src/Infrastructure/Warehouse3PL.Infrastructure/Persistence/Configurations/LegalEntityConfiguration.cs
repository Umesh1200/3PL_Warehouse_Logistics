using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("legal_entities");

        builder.HasKey(le => le.Id);
        builder.Property(le => le.Id)
            .HasColumnName("legal_entity_id")
            .ValueGeneratedNever();

        builder.Property(le => le.LegalCode)
            .HasColumnName("legal_code")
            .HasMaxLength(40)
            .IsRequired();

        builder.HasIndex(le => le.LegalCode)
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.Property(le => le.LegalName)
            .HasColumnName("legal_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(le => le.RegistrationNumber)
            .HasColumnName("registration_number")
            .HasMaxLength(80);

        builder.Property(le => le.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(le => le.TaxIdentifierType)
            .HasColumnName("tax_identifier_type")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(le => le.TaxRegistrationNumber)
            .HasColumnName("tax_registration_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(le => le.RegisteredAddressLine1)
            .HasColumnName("registered_address_line1")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(le => le.RegisteredAddressLine2)
            .HasColumnName("registered_address_line2")
            .HasMaxLength(255);

        builder.Property(le => le.RegisteredCity)
            .HasColumnName("registered_city")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(le => le.RegisteredStateOrProvince)
            .HasColumnName("registered_state_or_province")
            .HasMaxLength(100);

        builder.Property(le => le.RegisteredPostalCode)
            .HasColumnName("registered_postal_code")
            .HasMaxLength(30);

        builder.Property(le => le.IsCbmsMandated)
            .HasColumnName("is_cbms_mandated")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(le => le.IroSoftwarePermitRef)
            .HasColumnName("iro_software_permit_ref")
            .HasMaxLength(120);

        builder.Property(le => le.DefaultCurrencyCode)
            .HasColumnName("default_currency_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(le => le.ConfigRevision)
            .HasColumnName("config_revision")
            .IsConcurrencyToken()
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(le => le.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(le => le.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(le => le.DeletedAt).HasColumnName("deleted_at");
        builder.Property(le => le.DeletedBy).HasColumnName("deleted_by");
        builder.Property(le => le.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(le => le.CreatedBy).HasColumnName("created_by");
        builder.Property(le => le.UpdatedAt).HasColumnName("updated_at");
        builder.Property(le => le.UpdatedBy).HasColumnName("updated_by");

        builder.HasOne(le => le.CountryProfile)
            .WithMany()
            .HasForeignKey(le => le.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
