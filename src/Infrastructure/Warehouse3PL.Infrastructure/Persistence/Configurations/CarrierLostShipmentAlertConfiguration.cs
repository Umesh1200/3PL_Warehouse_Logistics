using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierLostShipmentAlertConfiguration : IEntityTypeConfiguration<CarrierLostShipmentAlert>
{
    public void Configure(EntityTypeBuilder<CarrierLostShipmentAlert> builder)
    {
        builder.ToTable("carrier_lost_shipment_alerts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("alert_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ShipmentId).HasColumnName("shipment_id").IsRequired();
        builder.Property(a => a.TrackingNumber).HasColumnName("tracking_number").HasMaxLength(100).IsRequired();
        builder.Property(a => a.CarrierCode).HasColumnName("carrier_code").HasMaxLength(50).IsRequired();
        builder.Property(a => a.CarrierServiceLevel).HasColumnName("carrier_service_level").HasConversion<string>().HasMaxLength(50).IsRequired();

        // Tenancy
        builder.Property(a => a.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(a => a.ClientId).HasColumnName("client_id").IsRequired();

        // Milestones
        builder.Property(a => a.DispatchedAtUtc).HasColumnName("dispatched_at_utc").IsRequired();
        builder.Property(a => a.ExpectedDeliveryDateUtc).HasColumnName("expected_delivery_date_utc").IsRequired(false);
        builder.Property(a => a.LastTrackingEventAtUtc).HasColumnName("last_tracking_event_at_utc").IsRequired(false);

        builder.Property(a => a.LastKnownStatus).HasColumnName("last_known_status").HasMaxLength(100).IsRequired();
        builder.Property(a => a.LastKnownLocation).HasColumnName("last_known_location").HasMaxLength(200).IsRequired();

        // Threshold Breach
        builder.Property(a => a.HoursWithoutScanMovement).HasColumnName("hours_without_scan_movement").HasPrecision(18, 2).IsRequired();
        builder.Property(a => a.PolicyThresholdHours).HasColumnName("policy_threshold_hours").HasPrecision(18, 2).IsRequired();
        builder.Property(a => a.TriggerReason).HasColumnName("trigger_reason").HasMaxLength(500).IsRequired();
        builder.Property(a => a.TriggeredAtUtc).HasColumnName("triggered_at_utc").IsRequired();

        // Status
        builder.Property(a => a.AlertStatus).HasColumnName("alert_status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(a => a.SlaPolicyVersion).HasColumnName("sla_policy_version").HasMaxLength(50).IsRequired();
        builder.Property(a => a.ResolutionNotes).HasColumnName("resolution_notes").HasMaxLength(1000).IsRequired();

        // Auditable Entity
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(a => a.ShipmentId)
            .HasDatabaseName("ix_carrier_lost_shipment_alert_shipment")
            .IsUnique();

        builder.HasIndex(a => a.TrackingNumber)
            .HasDatabaseName("ix_carrier_lost_shipment_alert_tracking");

        builder.HasIndex(a => new { a.FacilityId, a.ClientId, a.AlertStatus })
            .HasDatabaseName("ix_carrier_lost_shipment_alert_fac_client_status");
    }
}
