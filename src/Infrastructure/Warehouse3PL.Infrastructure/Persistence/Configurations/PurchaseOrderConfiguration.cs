using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");

        builder.HasKey(po => po.Id);
        builder.Property(po => po.Id)
            .HasColumnName("purchase_order_id")
            .ValueGeneratedNever();

        builder.Property(po => po.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(po => po.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(po => po.SupplierId)
            .HasColumnName("supplier_id");

        // Enforce tenant-aware PO number uniqueness
        builder.HasIndex(po => new { po.ClientId, po.PoNumber })
            .IsUnique();

        builder.Property(po => po.PoNumber)
            .HasColumnName("po_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(po => po.OrderDate)
            .HasColumnName("order_date")
            .IsRequired();

        builder.Property(po => po.ExpectedDeliveryDate)
            .HasColumnName("expected_delivery_date");

        builder.Property(po => po.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(po => po.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .HasDefaultValue("USD")
            .IsRequired();

        builder.Property(po => po.TotalEstimatedCost)
            .HasColumnName("total_estimated_cost")
            .HasPrecision(14, 2);

        builder.Property(po => po.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(po => po.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        builder.Property(po => po.CancelledAt)
            .HasColumnName("cancelled_at");

        // Optimistic concurrency token
        builder.Property(po => po.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(po => po.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(po => po.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(po => po.DeletedBy)
            .HasColumnName("deleted_by");

        builder.Property(po => po.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(po => po.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(po => po.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(po => po.UpdatedBy)
            .HasColumnName("updated_by");

        // Relationships
        builder.HasOne(po => po.Client)
            .WithMany()
            .HasForeignKey(po => po.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(po => po.Facility)
            .WithMany()
            .HasForeignKey(po => po.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(po => po.Supplier)
            .WithMany(s => s.PurchaseOrders)
            .HasForeignKey(po => po.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(po => po.Lines)
            .WithOne(l => l.PurchaseOrder)
            .HasForeignKey(l => l.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(po => po.Asns)
            .WithOne(a => a.PurchaseOrder)
            .HasForeignKey(a => a.PurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
