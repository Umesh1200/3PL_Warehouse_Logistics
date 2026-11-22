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

        builder.Property(x => x.WebhookTimestampUtc)
            .HasColumnName("webhook_timestamp_utc")
            .IsRequired();

        builder.Property(x => x.ReceivedAtUtc)
            .HasColumnName("received_at_utc")
            .IsRequired();

        builder.HasIndex(x => new { x.CarrierCode, x.Nonce })
            .HasDatabaseName("ix_carrier_webhook_nonces_carrier_code_nonce")
            .IsUnique();
    }
}
