using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("shipment_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.ShipmentNumber)
            .HasColumnName("shipment_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.DeliveryStatus)
            .HasColumnName("delivery_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(TrackingMilestoneStatus.PendingHandover)
            .IsRequired();

        builder.Property(s => s.DeliveryStatusUpdatedAtUtc)
            .HasColumnName("delivery_status_updated_at_utc");

        builder.Property(s => s.DeliveredAtUtc)
            .HasColumnName("delivered_at_utc");

        builder.Property(s => s.TotalPackagesCount)
            .HasColumnName("total_packages_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.TotalWeightKg)
            .HasColumnName("total_weight_kg")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(s => s.TotalVolumeCm3)
            .HasColumnName("total_volume_cm3")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(s => s.StagingLocationId)
            .HasColumnName("staging_location_id");

        builder.Property(s => s.AssignedDriverUserId)
            .HasColumnName("assigned_driver_user_id");

        builder.Property(s => s.TrailerNumber)
            .HasColumnName("trailer_number")
            .HasMaxLength(100);

        builder.Property(s => s.SealNumber)
            .HasColumnName("seal_number")
            .HasMaxLength(100);

        builder.Property(s => s.BillOfLadingNumber)
            .HasColumnName("bill_of_lading_number")
            .HasMaxLength(100);

        builder.Property(s => s.DispatchedAtUtc)
            .HasColumnName("dispatched_at_utc");

        builder.Property(s => s.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1u);

        // Soft delete
        builder.Property(s => s.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(s => s.DeletedAt).HasColumnName("deleted_at");
        builder.Property(s => s.DeletedBy).HasColumnName("deleted_by");

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(s => new { s.FacilityId, s.ShipmentNumber }).IsUnique();
        builder.HasIndex(s => s.ClientId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.StagingLocationId);
        builder.HasIndex(s => s.AssignedDriverUserId);
        builder.HasIndex(s => s.CarrierCode);

        // Relationships
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.StagingLocation)
            .WithMany()
            .HasForeignKey(s => s.StagingLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.AssignedDriverUser)
            .WithMany()
            .HasForeignKey(s => s.AssignedDriverUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(s => s.Packages)
            .WithOne(p => p.Shipment)
            .HasForeignKey(p => p.ShipmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

