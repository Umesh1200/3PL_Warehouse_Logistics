using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class WarehouseZoneConfiguration : IEntityTypeConfiguration<WarehouseZone>
{
    public void Configure(EntityTypeBuilder<WarehouseZone> builder)
    {
        builder.ToTable("warehouse_zones", t =>
        {
            t.HasCheckConstraint(
                "ck_warehouse_zones_temp_bounds",
                "temp_min_c IS NULL OR temp_max_c IS NULL OR temp_min_c <= temp_max_c");
        });

        builder.HasKey(z => z.Id);
        builder.HasAlternateKey(z => new { z.Id, z.FacilityId })
            .HasName("ak_warehouse_zones_zone_id_facility_id");

        builder.Property(z => z.Id)
            .HasColumnName("zone_id")
            .ValueGeneratedNever();

        builder.Property(z => z.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.HasIndex(z => z.FacilityId);

        builder.Property(z => z.ZoneCode)
            .HasColumnName("zone_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(z => z.NormalizedZoneCode)
            .HasColumnName("normalized_zone_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(z => new { z.FacilityId, z.NormalizedZoneCode })
            .IsUnique()
            .HasDatabaseName("ix_warehouse_zones_facility_normalized_code");

        builder.Property(z => z.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(z => z.ZoneType)
            .HasColumnName("zone_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(z => z.TempMinC)
            .HasColumnName("temp_min_c")
            .HasPrecision(5, 2);

        builder.Property(z => z.TempMaxC)
            .HasColumnName("temp_max_c")
            .HasPrecision(5, 2);

        builder.Property(z => z.DedicatedClientId)
            .HasColumnName("dedicated_client_id");

        builder.HasIndex(z => z.DedicatedClientId);

        builder.Property(z => z.Remarks)
            .HasColumnName("remarks")
            .HasMaxLength(500);

        builder.Property(z => z.ConfigRevision)
            .HasColumnName("config_revision")
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(z => z.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(z => z.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(z => z.DeletedAt).HasColumnName("deleted_at");
        builder.Property(z => z.DeletedBy).HasColumnName("deleted_by");
        builder.Property(z => z.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(z => z.CreatedBy).HasColumnName("created_by");
        builder.Property(z => z.UpdatedAt).HasColumnName("updated_at");
        builder.Property(z => z.UpdatedBy).HasColumnName("updated_by");

        // Relationships
        builder.HasOne(z => z.Facility)
            .WithMany(f => f.Zones)
            .HasForeignKey(z => z.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(z => z.DedicatedClient)
            .WithMany()
            .HasForeignKey(z => z.DedicatedClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

