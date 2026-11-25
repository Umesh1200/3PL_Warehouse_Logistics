using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CountryProfileConfiguration : IEntityTypeConfiguration<CountryProfile>
{
    public void Configure(EntityTypeBuilder<CountryProfile> builder)
    {
        builder.ToTable("country_profiles", t =>
            t.HasCheckConstraint(
                "ck_country_profiles_fiscal_month",
                "default_fiscal_year_start_month BETWEEN 1 AND 12"));

        builder.HasKey(c => c.CountryCode);
        builder.Property(c => c.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(2)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(c => c.IsoAlpha3Code)
            .HasColumnName("iso_alpha3_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.HasIndex(c => c.IsoAlpha3Code)
            .IsUnique();

        builder.Property(c => c.NumericCode)
            .HasColumnName("numeric_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(c => c.DefaultCurrencyCode)
            .HasColumnName("default_currency_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(c => c.PrimaryTimeZoneId)
            .HasColumnName("primary_time_zone_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(c => c.AddressSchemaType)
            .HasColumnName("address_schema_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(c => c.PostalCodeRegex)
            .HasColumnName("postal_code_regex")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(c => c.LegacyPostalCodeRegex)
            .HasColumnName("legacy_postal_code_regex")
            .HasMaxLength(120);

        builder.Property(c => c.PrimaryCalendarSystem)
            .HasColumnName("primary_calendar_system")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(c => c.FiscalCalendarSystem)
            .HasColumnName("fiscal_calendar_system")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(c => c.DefaultFiscalYearStartMonth)
            .HasColumnName("default_fiscal_year_start_month")
            .IsRequired();

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.SeedVersion)
            .HasColumnName("seed_version")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at");
    }
}
