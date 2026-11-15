using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReturnInboundHandoffConfiguration : IEntityTypeConfiguration<ReturnInboundHandoff>
{
    public void Configure(EntityTypeBuilder<ReturnInboundHandoff> builder)
    {
        builder.ToTable("return_inbound_handoffs");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasColumnName("handoff_id")
            .ValueGeneratedNever();

        builder.Property(h => h.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(h => h.DestinationFacilityId)
            .HasColumnName("destination_facility_id")
            .IsRequired();

        builder.Property(h => h.ReturnShipmentId)
            .HasColumnName("return_shipment_id")
            .IsRequired();

        builder.Property(h => h.ReturnAuthorizationId)
            .HasColumnName("return_authorization_id")
            .IsRequired();

        builder.Property(h => h.OriginalPackageId)
            .HasColumnName("original_package_id")
            .IsRequired();

        builder.Property(h => h.OriginalOrderId)
            .HasColumnName("original_order_id")
            .IsRequired();

        builder.Property(h => h.HandoffReference)
            .HasColumnName("handoff_reference")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(h => h.ReturnRmaNumber)
            .HasColumnName("return_rma_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(h => h.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(h => h.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(h => h.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(h => h.CarrierDeliveredAtUtc)
            .HasColumnName("carrier_delivered_at_utc")
            .IsRequired();

        builder.Property(h => h.HandoffCreatedAtUtc)
            .HasColumnName("handoff_created_at_utc")
            .IsRequired();

        builder.Property(h => h.DockDoorLocation)
            .HasColumnName("dock_door_location")
            .HasMaxLength(100);

        builder.Property(h => h.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(h => h.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(h => h.CreatedBy).HasColumnName("created_by");
        builder.Property(h => h.UpdatedAt).HasColumnName("updated_at");
        builder.Property(h => h.UpdatedBy).HasColumnName("updated_by");

        // Explicit Unique Indexes
        builder.HasIndex(h => h.ReturnShipmentId)
            .HasDatabaseName("ix_return_inbound_handoffs_shipment_id")
            .IsUnique();

        builder.HasIndex(h => h.ReturnAuthorizationId)
            .HasDatabaseName("ix_return_inbound_handoffs_auth_id")
            .IsUnique();

        builder.HasIndex(h => new { h.ClientId, h.HandoffReference })
            .HasDatabaseName("ix_return_inbound_handoffs_client_ref")
            .IsUnique();

        builder.HasIndex(h => new { h.DestinationFacilityId, h.Status })
            .HasDatabaseName("ix_return_inbound_handoffs_facility_status");

        // Relationships
        builder.HasOne(h => h.Client)
            .WithMany()
            .HasForeignKey(h => h.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.DestinationFacility)
            .WithMany()
            .HasForeignKey(h => h.DestinationFacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.ReturnShipment)
            .WithOne(s => s.InboundHandoff)
            .HasForeignKey<ReturnInboundHandoff>(h => h.ReturnShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.ReturnAuthorization)
            .WithOne(a => a.InboundHandoff)
            .HasForeignKey<ReturnInboundHandoff>(h => h.ReturnAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.OriginalPackage)
            .WithMany()
            .HasForeignKey(h => h.OriginalPackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.OriginalOrder)
            .WithMany()
            .HasForeignKey(h => h.OriginalOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

