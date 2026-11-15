using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class AdvanceShippingNoticeConfiguration : IEntityTypeConfiguration<AdvanceShippingNotice>
{
    public void Configure(EntityTypeBuilder<AdvanceShippingNotice> builder)
    {
        builder.ToTable("advance_shipping_notices");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("asn_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.PurchaseOrderId)
            .HasColumnName("purchase_order_id");

        builder.Property(a => a.SupplierId)
            .HasColumnName("supplier_id");

        // Enforce tenant-aware ASN number uniqueness
        builder.HasIndex(a => new { a.ClientId, a.AsnNumber })
            .IsUnique();

        builder.Property(a => a.AsnNumber)
            .HasColumnName("asn_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.SupplierName)
            .HasColumnName("supplier_name")
            .HasMaxLength(200);

        builder.Property(a => a.CarrierName)
            .HasColumnName("carrier_name")
            .HasMaxLength(150);

        builder.Property(a => a.TrackingReference)
            .HasColumnName("tracking_ref")
            .HasMaxLength(100);

        builder.Property(a => a.TrailerOrContainerNumber)
            .HasColumnName("trailer_container_number")
            .HasMaxLength(100);

        builder.Property(a => a.EstimatedArrival)
            .HasColumnName("expected_arrival");

        builder.Property(a => a.ActualArrival)
            .HasColumnName("actual_arrival");

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(a => a.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        builder.Property(a => a.CancelledAt)
            .HasColumnName("cancelled_at");

        // Optimistic concurrency token
        builder.Property(a => a.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(a => a.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(a => a.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(a => a.DeletedBy)
            .HasColumnName("deleted_by");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(a => a.UpdatedBy)
            .HasColumnName("updated_by");

        // Relationships
        builder.HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Facility)
            .WithMany()
            .HasForeignKey(a => a.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Supplier)
            .WithMany(s => s.Asns)
            .HasForeignKey(a => a.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(a => a.Lines)
            .WithOne(l => l.AdvanceShippingNotice)
            .HasForeignKey(l => l.AsnId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
