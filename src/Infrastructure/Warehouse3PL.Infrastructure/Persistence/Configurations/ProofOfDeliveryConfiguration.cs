using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ProofOfDeliveryConfiguration : IEntityTypeConfiguration<ProofOfDelivery>
{
    public void Configure(EntityTypeBuilder<ProofOfDelivery> builder)
    {
        builder.ToTable("proof_of_deliveries");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("pod_id")
            .ValueGeneratedNever();

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(p => p.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(p => p.ShipmentId)
            .HasColumnName("shipment_id");

        builder.Property(p => p.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.RecipientName)
            .HasColumnName("recipient_name")
            .HasMaxLength(150);

        builder.Property(p => p.DeliveryLocationDescription)
            .HasColumnName("delivery_location_description")
            .HasMaxLength(200);

        builder.Property(p => p.IsSigned)
            .HasColumnName("is_signed")
            .HasDefaultValue(false)
            .IsRequired();

        // Object storage references (Not base64 in PostgreSQL rows)
        builder.Property(p => p.SignatureObjectKey)
            .HasColumnName("signature_object_key")
            .HasMaxLength(300);

        builder.Property(p => p.PhotoObjectKey)
            .HasColumnName("photo_object_key")
            .HasMaxLength(300);

        builder.Property(p => p.SignatureContentType)
            .HasColumnName("signature_content_type")
            .HasMaxLength(100);

        builder.Property(p => p.PhotoContentType)
            .HasColumnName("photo_content_type")
            .HasMaxLength(100);

        builder.Property(p => p.SignatureSha256)
            .HasColumnName("signature_sha256")
            .HasMaxLength(64);

        builder.Property(p => p.PhotoSha256)
            .HasColumnName("photo_sha256")
            .HasMaxLength(64);

        builder.Property(p => p.Latitude)
            .HasColumnName("latitude")
            .HasPrecision(9, 6);

        builder.Property(p => p.Longitude)
            .HasColumnName("longitude")
            .HasPrecision(9, 6);

        builder.Property(p => p.CarrierConfirmationNumber)
            .HasColumnName("carrier_confirmation_number")
            .HasMaxLength(100);

        builder.Property(p => p.CarrierEventId)
            .HasColumnName("carrier_event_id")
            .HasMaxLength(100);

        builder.Property(p => p.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100);

        builder.Property(p => p.IsStorageFinalized)
            .HasColumnName("is_storage_finalized")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.EvidenceSource)
            .HasColumnName("evidence_source")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.VerificationReason)
            .HasColumnName("verification_reason")
            .HasMaxLength(500);

        builder.Property(p => p.ConsigneeAcknowledgmentText)
            .HasColumnName("consignee_acknowledgment_text")
            .HasMaxLength(1000);

        builder.Property(p => p.CapturedAtUtc)
            .HasColumnName("captured_at_utc")
            .IsRequired();

        builder.Property(p => p.DeliveredAtUtc)
            .HasColumnName("delivered_at_utc")
            .IsRequired();

        builder.Property(p => p.HasExplicitDeliveredAt)
            .HasColumnName("has_explicit_delivered_at")
            .HasDefaultValue(false)
            .IsRequired();

        // Supersession / amendment auditing
        builder.Property(p => p.IsSuperseded)
            .HasColumnName("is_superseded")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.SupersededByPodId)
            .HasColumnName("superseded_by_pod_id");

        builder.Property(p => p.SupersededReason)
            .HasColumnName("superseded_reason")
            .HasMaxLength(500);

        builder.Property(p => p.SupersededAtUtc)
            .HasColumnName("superseded_at_utc");

        // Auditing
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        // Unique constraint: Exactly one active authoritative POD per Package (permits superseded historical records)
        builder.HasIndex(p => p.PackageId)
            .IsUnique()
            .HasFilter("is_superseded = false")
            .HasDatabaseName("ix_proof_of_deliveries_package_unique");

        builder.HasIndex(p => new { p.ClientId, p.TrackingNumber })
            .HasDatabaseName("ix_proof_of_deliveries_client_tracking");

        builder.HasIndex(p => p.ShipmentId)
            .HasDatabaseName("ix_proof_of_deliveries_shipment");

        builder.HasIndex(p => new { p.ClientId, p.PackageId, p.IdempotencyKey })
            .HasDatabaseName("ix_proof_of_deliveries_client_package_idempotency");

        builder.HasIndex(p => new { p.FacilityId, p.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ix_proof_of_deliveries_facility_idempotency");

        builder.HasIndex(p => new { p.ClientId, p.CarrierConfirmationNumber })
            .HasDatabaseName("ix_proof_of_deliveries_client_carrier_conf");

        // Navigations
        builder.HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Facility)
            .WithMany()
            .HasForeignKey(p => p.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Package)
            .WithMany(pkg => pkg.ProofOfDeliveries)
            .HasForeignKey(p => p.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Shipment)
            .WithMany()
            .HasForeignKey(p => p.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

