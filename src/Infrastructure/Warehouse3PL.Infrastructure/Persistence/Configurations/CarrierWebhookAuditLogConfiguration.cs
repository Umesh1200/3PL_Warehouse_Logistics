using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierWebhookAuditLogConfiguration : IEntityTypeConfiguration<CarrierWebhookAuditLog>
{
    public void Configure(EntityTypeBuilder<CarrierWebhookAuditLog> builder)
    {
        builder.ToTable("carrier_webhook_audit_logs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("audit_log_id");

        builder.Property(x => x.CarrierCode).HasColumnName("carrier_code").HasMaxLength(50);
        builder.Property(x => x.TrackingNumber).HasColumnName("tracking_number").HasMaxLength(100);
        builder.Property(x => x.Nonce).HasColumnName("nonce").HasMaxLength(150);
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(60).IsRequired();
        builder.Property(x => x.HttpStatusCode).HasColumnName("http_status_code").IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.SourceIp).HasColumnName("source_ip").HasMaxLength(64);
        builder.Property(x => x.TraceId).HasColumnName("trace_id").HasMaxLength(128);
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();

        builder.HasIndex(x => new { x.CarrierCode, x.RecordedAtUtc })
            .HasDatabaseName("ix_carrier_webhook_audit_logs_carrier_recorded_at");
    }
}
