using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LaborStandardProfileConfiguration : IEntityTypeConfiguration<LaborStandardProfile>
{
    public void Configure(EntityTypeBuilder<LaborStandardProfile> builder)
    {
        builder.ToTable("labor_standard_profiles");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("profile_id")
            .ValueGeneratedNever();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired(false);

        builder.Property(p => p.ZoneId)
            .HasColumnName("zone_id")
            .IsRequired(false);

        builder.Property(p => p.ZoneType)
            .HasColumnName("zone_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(p => p.ActivityType)
            .HasColumnName("activity_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Department)
            .HasColumnName("department")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.BaseSetupSeconds).HasColumnName("base_setup_seconds").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.SecondsPerUnit).HasColumnName("seconds_per_unit").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.SecondsPerLine).HasColumnName("seconds_per_line").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.SecondsPerCarton).HasColumnName("seconds_per_carton").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.SecondsPerPallet).HasColumnName("seconds_per_pallet").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.SecondsPerTravelMeter).HasColumnName("seconds_per_travel_meter").HasPrecision(18, 4).IsRequired();

        builder.Property(p => p.EquipmentFactor).HasColumnName("equipment_factor").HasPrecision(18, 4).IsRequired();
        builder.Property(p => p.ComplexityMultiplier).HasColumnName("complexity_multiplier").HasPrecision(18, 4).IsRequired();
        builder.Property(p => p.Version).HasColumnName("version").IsRequired();

        builder.Property(p => p.EffectiveFromUtc).HasColumnName("effective_from_utc").IsRequired();
        builder.Property(p => p.EffectiveToUtc).HasColumnName("effective_to_utc").IsRequired(false);
        builder.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(p => new { p.FacilityId, p.ZoneId, p.ActivityType, p.IsActive })
            .HasDatabaseName("ix_labor_standard_profiles_facility_zone_activity");
    }
}
