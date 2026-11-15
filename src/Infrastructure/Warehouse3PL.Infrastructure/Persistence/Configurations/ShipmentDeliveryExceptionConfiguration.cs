using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ShipmentDeliveryExceptionConfiguration : IEntityTypeConfiguration<ShipmentDeliveryException>
{
    public void Configure(EntityTypeBuilder<ShipmentDeliveryException> builder)
    {
        builder.ToTable("shipment_delivery_exceptions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("exception_id")
            .ValueGeneratedNever();

        builder.Property(e => e.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(e => e.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(e => e.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(e => e.ShipmentId)
            .HasColumnName("shipment_id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id");

        builder.Property(e => e.ReturnShipmentId)
            .HasColumnName("return_shipment_id");

        builder.Property(e => e.ReturnAuthorizationId)
            .HasColumnName("return_authorization_id");

        builder.Property(e => e.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.ExceptionType)
            .HasColumnName("exception_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.CarrierExceptionCode)
            .HasColumnName("carrier_exception_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.CarrierDescription)
            .HasColumnName("carrier_description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        builder.Property(e => e.AttemptNumber)
            .HasColumnName("attempt_number")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(e => e.ReattemptCount)
            .HasColumnName("reattempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.ScheduledReattemptDateUtc)
            .HasColumnName("scheduled_reattempt_date_utc");

        builder.Property(e => e.ContactAttemptsCount)
            .HasColumnName("contact_attempts_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.CorrectedAddress)
            .HasColumnName("corrected_address")
            .HasMaxLength(500);

        builder.Property(e => e.HoldDepotLocation)
            .HasColumnName("hold_depot_location")
            .HasMaxLength(200);

        builder.Property(e => e.Resolution)
            .HasColumnName("resolution")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.ResolutionNotes)
            .HasColumnName("resolution_notes")
            .HasMaxLength(1000);

        builder.Property(e => e.ResolvedAtUtc)
            .HasColumnName("resolved_at_utc");

        builder.Property(e => e.ResolvedByUserId)
            .HasColumnName("resolved_by_user_id");

        // Ingestion & Audit Metadata (Sub-Step 3B.2)
        builder.Property(e => e.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.ReportedByUserId)
            .HasColumnName("reported_by_user_id");

        builder.Property(e => e.Latitude)
            .HasColumnName("latitude")
            .HasPrecision(10, 7);

        builder.Property(e => e.Longitude)
            .HasColumnName("longitude")
            .HasPrecision(10, 7);

        builder.Property(e => e.CarrierEventId)
            .HasColumnName("carrier_event_id")
            .HasMaxLength(100);

        builder.Property(e => e.RawPayload)
            .HasColumnName("raw_payload");

        builder.Property(e => e.EventFingerprint)
            .HasColumnName("event_fingerprint")
            .HasMaxLength(64);

        // Auditing
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(e => new { e.PackageId, e.OccurredAtUtc })
            .HasDatabaseName("ix_shipment_delivery_exceptions_pkg_occurred");

        builder.HasIndex(e => new { e.ClientId, e.ExceptionType })
            .HasDatabaseName("ix_shipment_delivery_exceptions_client_type");

        builder.HasIndex(e => new { e.ClientId, e.Status })
            .HasDatabaseName("ix_shipment_delivery_exceptions_client_status");

        builder.HasIndex(e => new { e.ClientId, e.ResolvedAtUtc })
            .HasDatabaseName("ix_shipment_delivery_exceptions_client_resolved");

        builder.HasIndex(e => e.OrderId)
            .HasDatabaseName("ix_shipment_delivery_exceptions_order_id");

        builder.HasIndex(e => new { e.ClientId, e.CarrierCode, e.EventFingerprint })
            .HasDatabaseName("ix_shipment_delivery_exceptions_fingerprint")
            .IsUnique()
            .HasFilter("event_fingerprint IS NOT NULL");

        // Navigations
        builder.HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Facility)
            .WithMany()
            .HasForeignKey(e => e.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Package)
            .WithMany(p => p.DeliveryExceptions)
            .HasForeignKey(e => e.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Shipment)
            .WithMany(s => s.DeliveryExceptions)
            .HasForeignKey(e => e.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Order)
            .WithMany(o => o.DeliveryExceptions)
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ResolvedByUser)
            .WithMany()
            .HasForeignKey(e => e.ResolvedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReportedByUser)
            .WithMany()
            .HasForeignKey(e => e.ReportedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReturnAuthorization)
            .WithMany()
            .HasForeignKey(e => e.ReturnAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
