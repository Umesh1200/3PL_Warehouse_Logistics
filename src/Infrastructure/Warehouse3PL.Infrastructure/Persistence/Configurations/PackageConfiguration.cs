using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("packages");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("package_id")
            .ValueGeneratedNever();

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(p => p.OutboundOrderId)
            .HasColumnName("outbound_order_id")
            .IsRequired();

        builder.Property(p => p.ShipmentId)
            .HasColumnName("shipment_id");

        builder.Property(p => p.HandlingUnitId)
            .HasColumnName("handling_unit_id")
            .IsRequired();

        builder.Property(p => p.PackagingMaterialId)
            .HasColumnName("packaging_material_id");

        builder.Property(p => p.PackageNumber)
            .HasColumnName("package_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.CartonSequence)
            .HasColumnName("carton_sequence")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(p => p.GrossWeightKg)
            .HasColumnName("gross_weight_kg")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.TareWeightKg)
            .HasColumnName("tare_weight_kg")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.NetWeightKg)
            .HasColumnName("net_weight_kg")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.LengthCm)
            .HasColumnName("length_cm")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.WidthCm)
            .HasColumnName("width_cm")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.HeightCm)
            .HasColumnName("height_cm")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.VolumetricDivisor)
            .HasColumnName("volumetric_divisor")
            .HasPrecision(14, 4)
            .HasDefaultValue(5000m)
            .IsRequired();

        builder.Property(p => p.VolumetricWeightKg)
            .HasColumnName("volumetric_weight_kg")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100);

        builder.Property(p => p.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50);

        builder.Property(p => p.CarrierServiceCode)
            .HasColumnName("carrier_service_code")
            .HasMaxLength(50);

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.CurrentTrackingStatus)
            .HasColumnName("current_tracking_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(TrackingMilestoneStatus.PendingHandover)
            .IsRequired();

        builder.Property(p => p.TrackingStatusUpdatedAtUtc)
            .HasColumnName("tracking_status_updated_at_utc");

        builder.Property(p => p.DeliveredAtUtc)
            .HasColumnName("delivered_at_utc");

        builder.Property(p => p.CurrentLocationId)
            .HasColumnName("current_location_id");

        builder.Property(p => p.PackedByUserId)
            .HasColumnName("packed_by_user_id");

        builder.Property(p => p.PackedAtUtc)
            .HasColumnName("packed_at_utc");

        builder.Property(p => p.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1u);

        // Soft delete
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        builder.Property(p => p.DeletedBy).HasColumnName("deleted_by");

        // Auditing
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(p => new { p.FacilityId, p.PackageNumber }).IsUnique();
        builder.HasIndex(p => p.HandlingUnitId).IsUnique();
        builder.HasIndex(p => p.OutboundOrderId);
        builder.HasIndex(p => p.ShipmentId);
        builder.HasIndex(p => p.TrackingNumber);
        builder.HasIndex(p => p.Status);

        // Relationships
        builder.HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Facility)
            .WithMany()
            .HasForeignKey(p => p.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.OutboundOrder)
            .WithMany(o => o.Packages)
            .HasForeignKey(p => p.OutboundOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Shipment)
            .WithMany(s => s.Packages)
            .HasForeignKey(p => p.ShipmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.HandlingUnit)
            .WithOne(h => h.Package)
            .HasForeignKey<Package>(p => p.HandlingUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PackagingMaterial)
            .WithMany()
            .HasForeignKey(p => p.PackagingMaterialId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.CurrentLocation)
            .WithMany()
            .HasForeignKey(p => p.CurrentLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.PackedByUser)
            .WithMany()
            .HasForeignKey(p => p.PackedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Lines)
            .WithOne(l => l.Package)
            .HasForeignKey(l => l.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ShippingLabels)
            .WithOne(sl => sl.Package)
            .HasForeignKey(sl => sl.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

