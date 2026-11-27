using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations", t =>
        {
            t.HasCheckConstraint("ck_locations_no_self_parent", "parent_location_id IS NULL OR parent_location_id <> location_id");
            t.HasCheckConstraint("ck_locations_hierarchy_level", "hierarchy_level >= 1 AND hierarchy_level <= 10");
        });

        builder.HasKey(l => l.Id);
        builder.HasAlternateKey(l => new { l.Id, l.FacilityId })
            .HasName("ak_locations_location_id_facility_id");

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

        builder.Property(l => l.NormalizedLocationCode)
            .HasColumnName("normalized_location_code")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(l => l.LocationName)
            .HasColumnName("location_name")
            .HasMaxLength(150);

        builder.Property(l => l.Barcode)
            .HasColumnName("barcode")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(l => l.NormalizedBarcode)
            .HasColumnName("normalized_barcode")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(l => l.HierarchyLevel)
            .HasColumnName("hierarchy_level")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(l => l.HierarchyPath)
            .HasColumnName("hierarchy_path")
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(l => l.Remarks)
            .HasColumnName("remarks")
            .HasMaxLength(500);

        builder.Property(l => l.ConfigRevision)
            .HasColumnName("config_revision")
            .HasDefaultValue(1L)
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

        // Status flags (A01: lifecycle Status orthogonal to IsLocked and IsDamaged)
        builder.Property(l => l.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(Domain.Enums.LocationStatus.Active)
            .IsRequired();
        builder.Property(l => l.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(l => l.IsStorable).HasColumnName("is_storable").HasDefaultValue(true).IsRequired();
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

        // Indexes (A03: permanent facility-scoped normalized uniqueness — no reuse of soft-deleted codes/barcodes)
        builder.HasIndex(l => l.FacilityId);
        builder.HasIndex(l => l.ZoneId);
        builder.HasIndex(l => l.ParentLocationId);
        builder.HasIndex(l => new { l.FacilityId, l.NormalizedLocationCode })
            .IsUnique()
            .HasDatabaseName("ix_locations_facility_normalized_code");
        builder.HasIndex(l => new { l.FacilityId, l.NormalizedBarcode })
            .IsUnique()
            .HasDatabaseName("ix_locations_facility_normalized_barcode");
        builder.HasIndex(l => new { l.FacilityId, l.HierarchyPath })
            .HasDatabaseName("ix_locations_facility_hierarchy_path");
        builder.HasIndex(l => new { l.FacilityId, l.Status, l.IsStorable })
            .HasDatabaseName("ix_locations_facility_status_storable");
        builder.HasIndex(l => l.PickSequence);

        // Relationships (A02/A05: composite same-facility FKs for Zone and ParentLocation)
        builder.HasOne(l => l.Facility)
            .WithMany(f => f.Locations)
            .HasForeignKey(l => l.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Zone)
            .WithMany(z => z.Locations)
            .HasForeignKey(l => new { l.ZoneId, l.FacilityId })
            .HasPrincipalKey(z => new { z.Id, z.FacilityId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.ParentLocation)
            .WithMany(p => p.ChildLocations)
            .HasForeignKey(l => new { l.ParentLocationId, l.FacilityId })
            .HasPrincipalKey(p => new { p.Id, p.FacilityId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

