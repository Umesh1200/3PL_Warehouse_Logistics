using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReturnShipmentConfiguration : IEntityTypeConfiguration<ReturnShipment>
{
    public void Configure(EntityTypeBuilder<ReturnShipment> builder)
    {
        builder.ToTable("return_shipments");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("return_shipment_id")
            .ValueGeneratedNever();

        builder.Property(r => r.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(r => r.OriginFacilityId)
            .HasColumnName("origin_facility_id")
            .IsRequired();

        builder.Property(r => r.DestinationFacilityId)
            .HasColumnName("destination_facility_id")
            .IsRequired();

        builder.Property(r => r.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(r => r.OriginalShipmentId)
            .HasColumnName("original_shipment_id");

        builder.Property(r => r.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(r => r.DeliveryExceptionId)
            .HasColumnName("delivery_exception_id");

        builder.Property(r => r.ReturnAuthorizationId)
            .HasColumnName("return_authorization_id");

        builder.Property(r => r.ReturnRmaNumber)
            .HasColumnName("return_rma_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.AuthorizedByUserId)
            .HasColumnName("authorized_by_user_id")
            .IsRequired();

        builder.Property(r => r.AuthorizedAtUtc)
            .HasColumnName("authorized_at_utc")
            .IsRequired();

        builder.Property(r => r.AuthorizationNotes)
            .HasColumnName("authorization_notes")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(r => r.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.CarrierServiceLevel)
            .HasColumnName("carrier_service_level")
            .HasMaxLength(50);

        builder.Property(r => r.LabelGeneratedAtUtc)
            .HasColumnName("label_generated_at_utc");

        builder.Property(r => r.CarrierShipmentId)
            .HasColumnName("carrier_shipment_id")
            .HasMaxLength(100);

        builder.Property(r => r.LabelFormat)
            .HasColumnName("label_format")
            .HasMaxLength(20)
            .HasDefaultValue("ZPL");

        builder.Property(r => r.LabelData)
            .HasColumnName("label_data");

        builder.Property(r => r.LabelObjectKey)
            .HasColumnName("label_object_key")
            .HasMaxLength(500);

        builder.Property(r => r.RateCharged)
            .HasColumnName("rate_charged")
            .HasColumnType("decimal(18,2)");

        builder.Property(r => r.Currency)
            .HasColumnName("currency")
            .HasMaxLength(10)
            .HasDefaultValue("USD");

        builder.Property(r => r.CarrierCancellationConfirmed)
            .HasColumnName("carrier_cancellation_confirmed");

        builder.Property(r => r.CarrierCancellationNotes)
            .HasColumnName("carrier_cancellation_notes")
            .HasMaxLength(1000);

        // Origin Address
        builder.Property(r => r.OriginAddressLine1)
            .HasColumnName("origin_address_line1")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.OriginAddressLine2)
            .HasColumnName("origin_address_line2")
            .HasMaxLength(200);

        builder.Property(r => r.OriginCity)
            .HasColumnName("origin_city")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.OriginState)
            .HasColumnName("origin_state")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.OriginPostalCode)
            .HasColumnName("origin_postal_code")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.OriginCountry)
            .HasColumnName("origin_country")
            .HasMaxLength(50)
            .IsRequired();

        // Destination Address
        builder.Property(r => r.DestinationAddressLine1)
            .HasColumnName("destination_address_line1")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.DestinationAddressLine2)
            .HasColumnName("destination_address_line2")
            .HasMaxLength(200);

        builder.Property(r => r.DestinationCity)
            .HasColumnName("destination_city")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.DestinationState)
            .HasColumnName("destination_state")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.DestinationPostalCode)
            .HasColumnName("destination_postal_code")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.DestinationCountry)
            .HasColumnName("destination_country")
            .HasMaxLength(50)
            .IsRequired();

        // Status
        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Milestones
        builder.Property(r => r.CarrierHandoverAtUtc)
            .HasColumnName("carrier_handover_at_utc");

        builder.Property(r => r.ExpectedWarehouseArrivalUtc)
            .HasColumnName("expected_warehouse_arrival_utc");

        builder.Property(r => r.DeliveredAtWarehouseUtc)
            .HasColumnName("delivered_at_warehouse_utc");

        builder.Property(r => r.ReceivedAndInspectedAtUtc)
            .HasColumnName("received_and_inspected_at_utc");

        builder.Property(r => r.InspectedByUserId)
            .HasColumnName("inspected_by_user_id");

        // Disposition
        builder.Property(r => r.FinalDisposition)
            .HasColumnName("final_disposition")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(r => r.DispositionNotes)
            .HasColumnName("disposition_notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(r => new { r.ClientId, r.ReturnRmaNumber })
            .HasDatabaseName("ix_return_shipments_rma")
            .IsUnique();

        builder.HasIndex(r => new { r.ClientId, r.Status })
            .HasDatabaseName("ix_return_shipments_client_status");

        builder.HasIndex(r => r.PackageId)
            .HasDatabaseName("ix_return_shipments_package_id");

        builder.HasIndex(r => new { r.DestinationFacilityId, r.Status })
            .HasDatabaseName("ix_return_shipments_dest_facility_status");

        builder.HasIndex(r => r.TrackingNumber)
            .HasDatabaseName("ix_return_shipments_tracking_number");

        builder.HasIndex(r => r.ReturnAuthorizationId)
            .HasDatabaseName("ix_return_shipments_auth_id")
            .IsUnique();

        builder.HasIndex(r => new { r.ClientId, r.TrackingNumber })
            .HasDatabaseName("ix_return_shipments_client_tracking")
            .IsUnique();

        // Navigations
        builder.HasOne(r => r.Client)
            .WithMany()
            .HasForeignKey(r => r.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.OriginFacility)
            .WithMany()
            .HasForeignKey(r => r.OriginFacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DestinationFacility)
            .WithMany()
            .HasForeignKey(r => r.DestinationFacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Order)
            .WithMany(o => o.ReturnShipments)
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.OriginalShipment)
            .WithMany(s => s.ReturnShipments)
            .HasForeignKey(r => r.OriginalShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Package)
            .WithMany(p => p.ReturnShipments)
            .HasForeignKey(r => r.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DeliveryException)
            .WithOne(e => e.ReturnShipment)
            .HasForeignKey<ReturnShipment>(r => r.DeliveryExceptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ReturnAuthorization)
            .WithOne(a => a.ReturnShipment)
            .HasForeignKey<ReturnShipment>(r => r.ReturnAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.AuthorizedByUser)
            .WithMany()
            .HasForeignKey(r => r.AuthorizedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.InspectedByUser)
            .WithMany()
            .HasForeignKey(r => r.InspectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

