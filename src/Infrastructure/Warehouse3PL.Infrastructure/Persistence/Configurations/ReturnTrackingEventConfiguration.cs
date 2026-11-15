using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReturnTrackingEventConfiguration : IEntityTypeConfiguration<ReturnTrackingEvent>
{
    public void Configure(EntityTypeBuilder<ReturnTrackingEvent> builder)
    {
        builder.ToTable("return_tracking_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(e => e.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(e => e.ReturnShipmentId)
            .HasColumnName("return_shipment_id")
            .IsRequired();

        builder.Property(e => e.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.CarrierStatusCode)
            .HasColumnName("carrier_status_code")
            .HasMaxLength(50);

        builder.Property(e => e.CarrierStatusDescription)
            .HasColumnName("carrier_status_description")
            .HasMaxLength(500);

        builder.Property(e => e.Location)
            .HasColumnName("location")
            .HasMaxLength(200);

        builder.Property(e => e.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        builder.Property(e => e.ReceivedAtUtc)
            .HasColumnName("received_at_utc")
            .IsRequired();

        builder.Property(e => e.EventFingerprint)
            .HasColumnName("event_fingerprint")
            .HasMaxLength(64)
            .IsRequired();

        // Auditing
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(e => new { e.ClientId, e.CarrierCode, e.EventFingerprint })
            .HasDatabaseName("ix_return_tracking_events_fingerprint")
            .IsUnique();

        builder.HasIndex(e => new { e.ReturnShipmentId, e.OccurredAtUtc })
            .HasDatabaseName("ix_return_tracking_events_shipment_occurred");

        // Navigation
        builder.HasOne(e => e.ReturnShipment)
            .WithMany(r => r.TrackingEvents)
            .HasForeignKey(e => e.ReturnShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

