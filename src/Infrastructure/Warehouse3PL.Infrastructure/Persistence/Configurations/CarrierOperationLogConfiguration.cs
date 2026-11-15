using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierOperationLogConfiguration : IEntityTypeConfiguration<CarrierOperationLog>
{
    public void Configure(EntityTypeBuilder<CarrierOperationLog> builder)
    {
        builder.ToTable("carrier_operation_logs");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("log_id")
            .ValueGeneratedNever();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(c => c.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(c => c.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(c => c.OperationType)
            .HasColumnName("operation_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.ServiceLevel)
            .HasColumnName("service_level")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.RequestPayload)
            .HasColumnName("request_payload")
            .IsRequired();

        builder.Property(c => c.ResponsePayload)
            .HasColumnName("response_payload");

        builder.Property(c => c.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100);

        builder.Property(c => c.ErrorMessage)
            .HasColumnName("error_message");

        builder.Property(c => c.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .IsRequired();

        builder.Property(c => c.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.HasOne(c => c.Package)
            .WithMany()
            .HasForeignKey(c => c.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.PackageId, c.IdempotencyKey });
        builder.HasIndex(c => new { c.ClientId, c.FacilityId, c.Status });
    }
}

