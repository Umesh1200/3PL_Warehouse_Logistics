using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Dock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DockDoorConfiguration : IEntityTypeConfiguration<DockDoor>
{
    public void Configure(EntityTypeBuilder<DockDoor> builder)
    {
        builder.ToTable("dock_doors", t =>
        {
            t.HasCheckConstraint(
                "ck_dock_doors_max_trailer_length_cm",
                "max_trailer_length_cm IS NULL OR max_trailer_length_cm > 0");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("dock_door_id")
            .ValueGeneratedNever();

        builder.Property(d => d.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(d => d.LocationId)
            .HasColumnName("location_id");

        builder.HasIndex(d => d.LocationId);

        builder.Property(d => d.DoorNumber)
            .HasColumnName("door_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(d => d.NormalizedDoorNumber)
            .HasColumnName("normalized_door_number")
            .HasMaxLength(50)
            .IsRequired();

        // Enforce permanent normalized door number uniqueness within a facility
        builder.HasIndex(d => new { d.FacilityId, d.NormalizedDoorNumber })
            .IsUnique()
            .HasDatabaseName("ix_dock_doors_facility_normalized_door_number");

        builder.Property(d => d.DoorType)
            .HasColumnName("door_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(d => d.SupportsRefrigerated)
            .HasColumnName("supports_refrigerated")
            .HasDefaultValue(false);

        builder.Property(d => d.MaxTrailerLengthCm)
            .HasColumnName("max_trailer_length_cm")
            .HasPrecision(10, 2);

        builder.Property(d => d.MaxTrailerLengthFeet)
            .HasColumnName("max_trailer_length_feet");

        builder.Property(d => d.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(d => d.ConfigRevision)
            .HasColumnName("config_revision")
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(d => d.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(d => d.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(d => d.DeletedBy)
            .HasColumnName("deleted_by");

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(d => d.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(d => d.UpdatedBy)
            .HasColumnName("updated_by");

        // Relationships
        builder.HasOne(d => d.Facility)
            .WithMany()
            .HasForeignKey(d => d.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.PhysicalLocation)
            .WithMany()
            .HasForeignKey(d => new { d.LocationId, d.FacilityId })
            .HasPrincipalKey(l => new { l.Id, l.FacilityId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.Appointments)
            .WithOne(a => a.DockDoor)
            .HasForeignKey(a => a.DockDoorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(d => d.Assignments)
            .WithOne(a => a.DockDoor)
            .HasForeignKey(a => a.DockDoorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

