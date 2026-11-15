using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ShipmentAuditLogConfiguration : IEntityTypeConfiguration<ShipmentAuditLog>
{
    public void Configure(EntityTypeBuilder<ShipmentAuditLog> builder)
    {
        builder.ToTable("shipment_audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("audit_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.ShipmentId)
            .HasColumnName("shipment_id")
            .IsRequired();

        builder.Property(a => a.PackageId)
            .HasColumnName("package_id");

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Details)
            .HasColumnName("details")
            .IsRequired();

        builder.Property(a => a.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(a => a.TimestampUtc)
            .HasColumnName("timestamp_utc")
            .IsRequired();

        builder.HasOne(a => a.Shipment)
            .WithMany()
            .HasForeignKey(a => a.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.ShipmentId, a.TimestampUtc });
    }
}

