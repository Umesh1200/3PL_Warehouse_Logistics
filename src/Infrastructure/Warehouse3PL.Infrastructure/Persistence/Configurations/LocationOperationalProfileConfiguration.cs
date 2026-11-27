using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LocationOperationalProfileConfiguration : IEntityTypeConfiguration<LocationOperationalProfile>
{
    public void Configure(EntityTypeBuilder<LocationOperationalProfile> builder)
    {
        builder.ToTable("location_operational_profiles", t =>
        {
            t.HasCheckConstraint("ck_location_operational_profiles_max_sessions", "max_concurrent_sessions >= 1");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("location_id").ValueGeneratedNever();
        builder.Ignore(p => p.LocationId);

        builder.Property(p => p.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(p => p.StationRole).HasColumnName("station_role").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(p => p.SupportsScale).HasColumnName("supports_scale").HasDefaultValue(false).IsRequired();
        builder.Property(p => p.SupportsLabelPrinter).HasColumnName("supports_label_printer").HasDefaultValue(false).IsRequired();
        builder.Property(p => p.SupportsConveyor).HasColumnName("supports_conveyor").HasDefaultValue(false).IsRequired();
        builder.Property(p => p.MaxConcurrentSessions).HasColumnName("max_concurrent_sessions").HasDefaultValue(1).IsRequired();
        builder.Property(p => p.DefaultStagingLocationId).HasColumnName("default_staging_location_id");
        builder.Property(p => p.ConfigRevision).HasColumnName("config_revision").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(p => p.FacilityId);

        // Composite same-facility FKs for both station location and default staging location (A05)
        builder.HasOne(p => p.Location)
            .WithOne(l => l.OperationalProfile)
            .HasForeignKey<LocationOperationalProfile>(p => new { p.Id, p.FacilityId })
            .HasPrincipalKey<Location>(l => new { l.Id, l.FacilityId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.DefaultStagingLocation)
            .WithMany()
            .HasForeignKey(p => new { p.DefaultStagingLocationId, p.FacilityId })
            .HasPrincipalKey(l => new { l.Id, l.FacilityId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Facility)
            .WithMany()
            .HasForeignKey(p => p.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
