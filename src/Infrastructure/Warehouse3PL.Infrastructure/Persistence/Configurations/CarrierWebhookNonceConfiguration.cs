using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierWebhookNonceConfiguration : IEntityTypeConfiguration<CarrierWebhookNonce>
{
    public void Configure(EntityTypeBuilder<CarrierWebhookNonce> builder)
    {
        builder.ToTable("carrier_webhook_nonces");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("nonce_id");

        builder.Property(x => x.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Nonce)
            .HasColumnName("nonce")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.PayloadSha256)
            .HasColumnName("payload_sha256")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CarrierWebhookNonceStatus.Processing)
            .IsRequired();

        builder.Property(x => x.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(x => x.WebhookTimestampUtc)
            .HasColumnName("webhook_timestamp_utc")
            .IsRequired();

        builder.Property(x => x.ReceivedAtUtc)
            .HasColumnName("received_at_utc")
            .IsRequired();

        builder.Property(x => x.LastAttemptAtUtc)
            .HasColumnName("last_attempt_at_utc")
            .IsRequired();

        builder.Property(x => x.ProcessingLeaseExpiresAtUtc)
            .HasColumnName("processing_lease_expires_at_utc");

        builder.Property(x => x.ProcessedAtUtc)
            .HasColumnName("processed_at_utc");

        builder.Property(x => x.LastHttpStatusCode)
            .HasColumnName("last_http_status_code");

        builder.Property(x => x.LastFailureReason)
            .HasColumnName("last_failure_reason")
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.CarrierCode, x.Nonce })
            .HasDatabaseName("ix_carrier_webhook_nonces_carrier_code_nonce")
            .IsUnique();
    }
}
