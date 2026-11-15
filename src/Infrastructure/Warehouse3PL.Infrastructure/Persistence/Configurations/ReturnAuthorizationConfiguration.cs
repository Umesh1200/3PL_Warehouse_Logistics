using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReturnAuthorizationConfiguration : IEntityTypeConfiguration<ReturnAuthorization>
{
    public void Configure(EntityTypeBuilder<ReturnAuthorization> builder)
    {
        builder.ToTable("return_authorizations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("return_authorization_id")
            .ValueGeneratedNever();

        builder.Property(r => r.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(r => r.OriginFacilityId)
            .HasColumnName("origin_facility_id")
            .IsRequired();

        builder.Property(r => r.DestinationFacilityId)
            .HasColumnName("destination_facility_id")
            .IsRequired();

        builder.Property(r => r.AuthorizationNumber)
            .HasColumnName("authorization_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.RmaNumber)
            .HasColumnName("rma_number")
            .HasMaxLength(50);

        builder.Property(r => r.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(r => r.OriginalShipmentId)
            .HasColumnName("original_shipment_id");

        builder.Property(r => r.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(r => r.DeliveryExceptionId)
            .HasColumnName("delivery_exception_id");

        builder.Property(r => r.ReturnShipmentId)
            .HasColumnName("return_shipment_id");

        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.AuthorizedByUserId)
            .HasColumnName("authorized_by_user_id")
            .IsRequired();

        builder.Property(r => r.AuthorizedAtUtc)
            .HasColumnName("authorized_at_utc")
            .IsRequired();

        builder.Property(r => r.ExpiresAtUtc)
            .HasColumnName("expires_at_utc");

        builder.Property(r => r.AuthorizationNotes)
            .HasColumnName("authorization_notes")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(r => r.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(1000);

        builder.Property(r => r.CancelledAtUtc)
            .HasColumnName("cancelled_at_utc");

        builder.Property(r => r.CancelledByUserId)
            .HasColumnName("cancelled_by_user_id");

        // Auditing
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(r => new { r.ClientId, r.AuthorizationNumber })
            .HasDatabaseName("ix_return_authorizations_client_auth_num")
            .IsUnique();

        builder.HasIndex(r => new { r.ClientId, r.Status })
            .HasDatabaseName("ix_return_authorizations_client_status");

        builder.HasIndex(r => new { r.PackageId, r.Status })
            .HasDatabaseName("ix_return_authorizations_package_status");

        builder.HasIndex(r => new { r.DestinationFacilityId, r.Status })
            .HasDatabaseName("ix_return_authorizations_dest_facility_status");

        builder.HasIndex(r => r.DeliveryExceptionId)
            .HasDatabaseName("ix_return_authorizations_exception_id");

        builder.HasIndex(r => r.ReturnShipmentId)
            .HasDatabaseName("ix_return_authorizations_return_shipment_id");

        // Relationships
        builder.HasOne(r => r.Client)
            .WithMany()
            .HasForeignKey(r => r.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.OriginFacility)
            .WithMany()
            .HasForeignKey(r => r.OriginFacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DestinationFacility)
            .WithMany()
            .HasForeignKey(r => r.DestinationFacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Order)
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.OriginalShipment)
            .WithMany()
            .HasForeignKey(r => r.OriginalShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Package)
            .WithMany()
            .HasForeignKey(r => r.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DeliveryException)
            .WithMany()
            .HasForeignKey(r => r.DeliveryExceptionId)
            .OnDelete(DeleteBehavior.Restrict);

        // ReturnShipment 1-to-1 relationship is configured in ReturnShipmentConfiguration via ReturnAuthorizationId.

        builder.HasOne(r => r.AuthorizedByUser)
            .WithMany()
            .HasForeignKey(r => r.AuthorizedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CancelledByUser)
            .WithMany()
            .HasForeignKey(r => r.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
