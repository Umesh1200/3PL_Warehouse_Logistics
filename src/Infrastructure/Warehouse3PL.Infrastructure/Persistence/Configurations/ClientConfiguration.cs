using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("clients");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("client_id")
            .ValueGeneratedNever();

        builder.Property(c => c.CompanyName)
            .HasColumnName("company_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.AccountCode)
            .HasColumnName("account_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.NormalizedAccountCode)
            .HasColumnName("normalized_account_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(c => c.AccountCode)
            .IsUnique();

        builder.HasIndex(c => c.NormalizedAccountCode)
            .IsUnique()
            .HasDatabaseName("ux_clients_normalized_account_code");

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Enforce B-Tree index on status per DB-SPEC-3PL-2026
        builder.HasIndex(c => c.Status);

        builder.Property(c => c.SlaTier)
            .HasColumnName("sla_tier")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(c => c.BusinessClassification)
            .HasColumnName("business_classification")
            .HasMaxLength(100);

        builder.Property(c => c.TaxRegistrationNumber)
            .HasColumnName("tax_registration_number")
            .HasMaxLength(64);

        builder.Property(c => c.BillingAddressLine1)
            .HasColumnName("billing_address_line_1")
            .HasMaxLength(255);

        builder.Property(c => c.BillingAddressLine2)
            .HasColumnName("billing_address_line_2")
            .HasMaxLength(255);

        builder.Property(c => c.BillingCity)
            .HasColumnName("billing_city")
            .HasMaxLength(100);

        builder.Property(c => c.BillingStateOrProvince)
            .HasColumnName("billing_state_or_province")
            .HasMaxLength(100);

        builder.Property(c => c.BillingPostalCode)
            .HasColumnName("billing_postal_code")
            .HasMaxLength(30);

        builder.Property(c => c.BillingCountryCode)
            .HasColumnName("billing_country_code")
            .HasMaxLength(50);

        builder.Property(c => c.CountryNormalizationStatus)
            .HasColumnName("country_normalization_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CountryNormalizationStatus.PendingNormalization);

        builder.Property(c => c.ConfigRevision)
            .HasColumnName("config_revision")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(c => c.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");
        builder.Property(c => c.DeletedBy).HasColumnName("deleted_by");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.UpdatedBy).HasColumnName("updated_by");

        // Relationships
        builder.HasMany(c => c.Contacts)
            .WithOne(cc => cc.Client)
            .HasForeignKey(cc => cc.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Users)
            .WithOne(u => u.Client)
            .HasForeignKey(u => u.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.FacilityAccesses)
            .WithOne(cfa => cfa.Client)
            .HasForeignKey(cfa => cfa.ClientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

