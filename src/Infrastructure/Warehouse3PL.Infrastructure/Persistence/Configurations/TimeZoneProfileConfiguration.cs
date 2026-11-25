using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class TimeZoneProfileConfiguration : IEntityTypeConfiguration<TimeZoneProfile>
{
    public void Configure(EntityTypeBuilder<TimeZoneProfile> builder)
    {
        builder.ToTable("time_zone_profiles");

        builder.HasKey(tz => tz.TimeZoneId);
        builder.Property(tz => tz.TimeZoneId)
            .HasColumnName("time_zone_id")
            .HasMaxLength(64)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(tz => tz.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(tz => tz.ObservesDst)
            .HasColumnName("observes_dst")
            .IsRequired();

        builder.Property(tz => tz.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(2);

        builder.Property(tz => tz.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(tz => tz.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(tz => tz.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(tz => tz.CountryCode);
    }
}
