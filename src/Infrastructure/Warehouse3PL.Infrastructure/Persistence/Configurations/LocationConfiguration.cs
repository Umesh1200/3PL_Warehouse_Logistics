using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("location_id")
            .ValueGeneratedNever();

        builder.Property(l => l.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(l => l.ZoneId)
            .HasColumnName("zone_id");

        builder.Property(l => l.ParentLocationId)
            .HasColumnName("parent_location_id");

        builder.Property(l => l.LocationType)
            .HasColumnName("location_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.LocationCode)
            .HasColumnName("location_code")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(l => l.Barcode)
            .HasColumnName("barcode")
            .HasMaxLength(100)
            .IsRequired();

        // Coordinates & Hierarchy Path
        builder.Property(l => l.AisleCode).HasColumnName("aisle_code").HasMaxLength(20);
        builder.Property(l => l.RackCode).HasColumnName("rack_code").HasMaxLength(20);
        builder.Property(l => l.ShelfLevel).HasColumnName("shelf_level");
        builder.Property(l => l.BinNumber).HasColumnName("bin_number");
        builder.Property(l => l.XCoord).HasColumnName("x_coord").HasPrecision(10, 4);
        builder.Property(l => l.YCoord).HasColumnName("y_coord").HasPrecision(10, 4);
        builder.Property(l => l.ZCoord).HasColumnName("z_coord").HasPrecision(10, 4);
        builder.Property(l => l.PickSequence).HasColumnName("pick_sequence");

        // Capacities
        builder.Property(l => l.LengthCm).HasColumnName("length_cm").HasPrecision(10, 2);
        builder.Property(l => l.WidthCm).HasColumnName("width_cm").HasPrecision(10, 2);
        builder.Property(l => l.HeightCm).HasColumnName("height_cm").HasPrecision(10, 2);
        builder.Property(l => l.MaxVolumeCm3).HasColumnName("max_vol_cm3").HasPrecision(14, 2);
        builder.Property(l => l.MaxWeightKg).HasColumnName("max_weight_kg").HasPrecision(10, 2);

        // Status flags
        builder.Property(l => l.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(l => l.IsPickable).HasColumnName("is_pickable").HasDefaultValue(true);
        builder.Property(l => l.IsReceiving).HasColumnName("is_receiving").HasDefaultValue(false);
        builder.Property(l => l.IsDamaged).HasColumnName("is_damaged").HasDefaultValue(false);
        builder.Property(l => l.IsLocked).HasColumnName("is_locked").HasDefaultValue(false);

        // Soft delete & Auditing
        builder.Property(l => l.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(l => l.DeletedAt).HasColumnName("deleted_at");
        builder.Property(l => l.DeletedBy).HasColumnName("deleted_by");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.CreatedBy).HasColumnName("created_by");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(l => l.FacilityId);
        builder.HasIndex(l => l.ZoneId);
        builder.HasIndex(l => l.ParentLocationId);
        builder.HasIndex(l => new { l.FacilityId, l.LocationCode }).IsUnique();
        builder.HasIndex(l => new { l.FacilityId, l.Barcode }).IsUnique();
        builder.HasIndex(l => l.PickSequence);

        // Relationships
        builder.HasOne(l => l.Facility)
            .WithMany(f => f.Locations)
            .HasForeignKey(l => l.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Zone)
            .WithMany(z => z.Locations)
            .HasForeignKey(l => l.ZoneId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.ParentLocation)
            .WithMany(p => p.ChildLocations)
            .HasForeignKey(l => l.ParentLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

