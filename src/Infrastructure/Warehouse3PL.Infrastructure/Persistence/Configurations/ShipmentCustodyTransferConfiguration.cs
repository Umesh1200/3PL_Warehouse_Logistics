using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ShipmentCustodyTransferConfiguration : IEntityTypeConfiguration<ShipmentCustodyTransfer>
{
    public void Configure(EntityTypeBuilder<ShipmentCustodyTransfer> builder)
    {
        builder.ToTable("shipment_custody_transfers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("transfer_id")
            .ValueGeneratedNever();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(c => c.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(c => c.ShipmentId)
            .HasColumnName("shipment_id")
            .IsRequired();

        builder.Property(c => c.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.DriverName)
            .HasColumnName("driver_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(c => c.DriverLicenseNumber)
            .HasColumnName("driver_license_number")
            .HasMaxLength(100);

        builder.Property(c => c.DriverSignatureAcknowledgment)
            .HasColumnName("driver_signature_acknowledgment")
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(c => c.TrailerNumber)
            .HasColumnName("trailer_number")
            .HasMaxLength(50);

        builder.Property(c => c.SealNumber)
            .HasColumnName("seal_number")
            .HasMaxLength(50);

        builder.Property(c => c.BillOfLadingNumber)
            .HasColumnName("bill_of_lading_number")
            .HasMaxLength(100);

        builder.Property(c => c.TotalPackagesConfirmed)
            .HasColumnName("total_packages_confirmed")
            .IsRequired();

        builder.Property(c => c.TotalWeightKg)
            .HasColumnName("total_weight_kg")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(c => c.AuthorizedByUserId)
            .HasColumnName("authorized_by_user_id")
            .IsRequired();

        builder.Property(c => c.AssignedDriverUserId)
            .HasColumnName("assigned_driver_user_id");

        builder.Property(c => c.CustodyStatus)
            .HasColumnName("custody_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(ShipmentCustodyStatus.Confirmed)
            .IsRequired();

        builder.Property(c => c.TransferredAtUtc)
            .HasColumnName("transferred_at_utc")
            .IsRequired();

        builder.HasOne(c => c.Shipment)
            .WithMany()
            .HasForeignKey(c => c.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.AssignedDriverUser)
            .WithMany()
            .HasForeignKey(c => c.AssignedDriverUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.ShipmentId);
        builder.HasIndex(c => c.AssignedDriverUserId);
        builder.HasIndex(c => new { c.FacilityId, c.TransferredAtUtc });
    }
}

