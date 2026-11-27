using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LocationStorageProfileConfiguration : IEntityTypeConfiguration<LocationStorageProfile>
{
    public void Configure(EntityTypeBuilder<LocationStorageProfile> builder)
    {
        builder.ToTable("location_storage_profiles", t =>
        {
            t.HasCheckConstraint("ck_location_storage_profiles_max_hu", "max_handling_units IS NULL OR max_handling_units > 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("location_id").ValueGeneratedNever();
        builder.Ignore(p => p.LocationId);

        builder.Property(p => p.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(p => p.DedicatedClientId).HasColumnName("dedicated_client_id");
        builder.Property(p => p.MaxHandlingUnits).HasColumnName("max_handling_units");
        builder.Property(p => p.AllowMixedSku).HasColumnName("allow_mixed_sku").HasDefaultValue(true).IsRequired();
        builder.Property(p => p.AllowMixedLot).HasColumnName("allow_mixed_lot").HasDefaultValue(true).IsRequired();
        builder.Property(p => p.IsPutawayAllowed).HasColumnName("is_putaway_allowed").HasDefaultValue(true).IsRequired();
        builder.Property(p => p.ConfigRevision).HasColumnName("config_revision").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(p => p.FacilityId);
        builder.HasIndex(p => p.DedicatedClientId);

        // Composite same-facility FK to locations(location_id, facility_id) (A05)
        builder.HasOne(p => p.Location)
            .WithOne(l => l.StorageProfile)
            .HasForeignKey<LocationStorageProfile>(p => new { p.Id, p.FacilityId })
            .HasPrincipalKey<Location>(l => new { l.Id, l.FacilityId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Facility)
            .WithMany()
            .HasForeignKey(p => p.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.DedicatedClient)
            .WithMany()
            .HasForeignKey(p => p.DedicatedClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
