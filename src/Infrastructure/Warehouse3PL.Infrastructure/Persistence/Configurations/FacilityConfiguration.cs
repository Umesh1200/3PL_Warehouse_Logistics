using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class FacilityConfiguration : IEntityTypeConfiguration<Facility>
{
    public void Configure(EntityTypeBuilder<Facility> builder)
    {
        builder.ToTable("facilities");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasColumnName("facility_id")
            .ValueGeneratedNever();

        builder.Property(f => f.FacilityCode)
            .HasColumnName("facility_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(f => f.FacilityCode)
            .IsUnique();

        builder.Property(f => f.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(f => f.AddressLine1)
            .HasColumnName("address_line1")
            .HasMaxLength(255);

        builder.Property(f => f.AddressLine2)
            .HasColumnName("address_line2")
            .HasMaxLength(255);

        builder.Property(f => f.City)
            .HasColumnName("city")
            .HasMaxLength(100);

        builder.Property(f => f.StateOrProvince)
            .HasColumnName("state_or_province")
            .HasMaxLength(100);

        builder.Property(f => f.PostalCode)
            .HasColumnName("postal_code")
            .HasMaxLength(30);

        builder.Property(f => f.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(3)
            .HasDefaultValue("USA");

        builder.Property(f => f.ContactEmail)
            .HasColumnName("contact_email")
            .HasMaxLength(255);

        builder.Property(f => f.ContactPhone)
            .HasColumnName("contact_phone")
            .HasMaxLength(50);

        builder.Property(f => f.TotalAreaSqM)
            .HasColumnName("total_area_sq_m")
            .HasPrecision(12, 2);

        builder.Property(f => f.DockDoorCount)
            .HasColumnName("dock_door_count")
            .HasDefaultValue(4);

        builder.Property(f => f.OperatingHoursJson)
            .HasColumnName("operating_hours_json");

        builder.Property(f => f.ConfigurationJson)
            .HasColumnName("configuration_json");

        builder.Property(f => f.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(f => f.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(f => f.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(f => f.DeletedAt).HasColumnName("deleted_at");
        builder.Property(f => f.DeletedBy).HasColumnName("deleted_by");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(f => f.CreatedBy).HasColumnName("created_by");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.Property(f => f.UpdatedBy).HasColumnName("updated_by");

        // Relationships
        builder.HasMany(f => f.ClientAccesses)
            .WithOne(cfa => cfa.Facility)
            .HasForeignKey(cfa => cfa.FacilityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

