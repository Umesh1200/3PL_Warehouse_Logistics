using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ShipmentTrackingEventConfiguration : IEntityTypeConfiguration<ShipmentTrackingEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentTrackingEvent> builder)
    {
        builder.ToTable("shipment_tracking_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("tracking_event_id")
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

        builder.Property(e => e.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.CarrierEventId)
            .HasColumnName("carrier_event_id")
            .HasMaxLength(100);

        builder.Property(e => e.MilestoneStatus)
            .HasColumnName("milestone_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.CarrierEventCode)
            .HasColumnName("carrier_event_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.CarrierDescription)
            .HasColumnName("carrier_description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.EventCity)
            .HasColumnName("event_city")
            .HasMaxLength(100);

        builder.Property(e => e.EventStateOrProvince)
            .HasColumnName("event_state_or_province")
            .HasMaxLength(100);

        builder.Property(e => e.EventPostalCode)
            .HasColumnName("event_postal_code")
            .HasMaxLength(30);

        builder.Property(e => e.EventCountry)
            .HasColumnName("event_country")
            .HasMaxLength(10);

        builder.Property(e => e.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        builder.Property(e => e.ReceivedAtUtc)
            .HasColumnName("received_at_utc")
            .IsRequired();

        builder.Property(e => e.EventFingerprint)
            .HasColumnName("event_fingerprint")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.RawPayloadJson)
            .HasColumnName("raw_payload_json")
            .HasColumnType("jsonb");

        // Database constraint: Unique carrier event fingerprint per tenant
        builder.HasIndex(e => new { e.ClientId, e.CarrierCode, e.EventFingerprint })
            .IsUnique()
            .HasDatabaseName("ix_shipment_tracking_events_tenant_fingerprint");

        builder.HasIndex(e => new { e.PackageId, e.OccurredAtUtc })
            .HasDatabaseName("ix_shipment_tracking_events_pkg_occurred");

        builder.HasIndex(e => new { e.TrackingNumber, e.OccurredAtUtc })
            .HasDatabaseName("ix_shipment_tracking_events_tracking_occurred");

        builder.HasIndex(e => new { e.ClientId, e.MilestoneStatus })
            .HasDatabaseName("ix_shipment_tracking_events_client_status");

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
            .WithMany(p => p.TrackingEvents)
            .HasForeignKey(e => e.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Shipment)
            .WithMany(s => s.TrackingEvents)
            .HasForeignKey(e => e.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

