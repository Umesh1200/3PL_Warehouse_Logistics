using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PublicTrackingShareConfiguration : IEntityTypeConfiguration<PublicTrackingShare>
{
    public void Configure(EntityTypeBuilder<PublicTrackingShare> builder)
    {
        builder.ToTable("public_tracking_shares");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("share_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(s => s.ShipmentId)
            .HasColumnName("shipment_id");

        builder.Property(s => s.PublicToken)
            .HasColumnName("public_token")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(s => s.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.MaskedRecipientName)
            .HasColumnName("masked_recipient_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(s => s.DestinationCity)
            .HasColumnName("destination_city")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.DestinationState)
            .HasColumnName("destination_state")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.MaskedPostalCode)
            .HasColumnName("masked_postal_code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(s => s.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired();

        builder.Property(s => s.RevokedAtUtc)
            .HasColumnName("revoked_at_utc");

        builder.Property(s => s.AccessCount)
            .HasColumnName("access_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.MaxAccessCount)
            .HasColumnName("max_access_count")
            .HasDefaultValue(500)
            .IsRequired();

        // Unique index on cryptographically random public token
        builder.HasIndex(s => s.PublicToken)
            .IsUnique()
            .HasDatabaseName("ix_public_tracking_shares_token_unique");

        builder.HasIndex(s => s.PackageId)
            .HasDatabaseName("ix_public_tracking_shares_package");

        builder.HasIndex(s => new { s.TrackingNumber, s.ExpiresAtUtc })
            .HasDatabaseName("ix_public_tracking_shares_tracking_expires");

        // Navigations
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Package)
            .WithMany(p => p.PublicTrackingShares)
            .HasForeignKey(s => s.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Shipment)
            .WithMany()
            .HasForeignKey(s => s.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

