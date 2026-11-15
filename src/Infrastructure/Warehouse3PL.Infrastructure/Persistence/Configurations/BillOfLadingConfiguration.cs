using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class BillOfLadingConfiguration : IEntityTypeConfiguration<BillOfLading>
{
    public void Configure(EntityTypeBuilder<BillOfLading> builder)
    {
        builder.ToTable("bills_of_lading");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasColumnName("bol_id")
            .ValueGeneratedNever();

        builder.Property(b => b.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(b => b.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(b => b.ShipmentId)
            .HasColumnName("shipment_id")
            .IsRequired();

        builder.Property(b => b.BolNumber)
            .HasColumnName("bol_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(b => b.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(b => b.CarrierName)
            .HasColumnName("carrier_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(b => b.ServiceLevel)
            .HasColumnName("service_level")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(b => b.TrailerNumber)
            .HasColumnName("trailer_number")
            .HasMaxLength(50);

        builder.Property(b => b.SealNumber)
            .HasColumnName("seal_number")
            .HasMaxLength(50);

        builder.Property(b => b.TotalPackageCount)
            .HasColumnName("total_package_count")
            .IsRequired();

        builder.Property(b => b.TotalGrossWeightKg)
            .HasColumnName("total_gross_weight_kg")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(b => b.TotalVolumeCm3)
            .HasColumnName("total_volume_cm3")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(b => b.ShipperName)
            .HasColumnName("shipper_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(b => b.ShipperAddressSnapshot)
            .HasColumnName("shipper_address_snapshot")
            .IsRequired();

        builder.Property(b => b.ConsigneeName)
            .HasColumnName("consignee_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(b => b.ConsigneeAddressSnapshot)
            .HasColumnName("consignee_address_snapshot")
            .IsRequired();

        builder.Property(b => b.PackageTrackingNumbers)
            .HasColumnName("package_tracking_numbers")
            .IsRequired();

        builder.Property(b => b.DocumentSnapshotJson)
            .HasColumnName("document_snapshot_json")
            .IsRequired();

        builder.Property(b => b.IssuedAtUtc)
            .HasColumnName("issued_at_utc")
            .IsRequired();

        builder.HasOne(b => b.Shipment)
            .WithMany()
            .HasForeignKey(b => b.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => new { b.FacilityId, b.BolNumber }).IsUnique();
        builder.HasIndex(b => b.ShipmentId);
    }
}

