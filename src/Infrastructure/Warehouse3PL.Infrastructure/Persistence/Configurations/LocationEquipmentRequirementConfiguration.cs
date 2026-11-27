using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LocationEquipmentRequirementConfiguration : IEntityTypeConfiguration<LocationEquipmentRequirement>
{
    public void Configure(EntityTypeBuilder<LocationEquipmentRequirement> builder)
    {
        builder.ToTable("location_equipment_requirements");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("requirement_id").ValueGeneratedNever();
        builder.Property(r => r.LocationId).HasColumnName("location_id").IsRequired();
        builder.Property(r => r.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(r => r.EquipmentType).HasColumnName("equipment_type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(r => r.IsMandatory).HasColumnName("is_mandatory").HasDefaultValue(true).IsRequired();
        builder.Property(r => r.ConfigRevision).HasColumnName("config_revision").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(r => new { r.LocationId, r.EquipmentType })
            .HasDatabaseName("uq_location_equipment_requirements_loc_type")
            .IsUnique();

        // Composite same-facility FK to locations(location_id, facility_id) (A05)
        builder.HasOne(r => r.Location)
            .WithMany(l => l.EquipmentRequirements)
            .HasForeignKey(r => new { r.LocationId, r.FacilityId })
            .HasPrincipalKey(l => new { l.Id, l.FacilityId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Facility)
            .WithMany()
            .HasForeignKey(r => r.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
