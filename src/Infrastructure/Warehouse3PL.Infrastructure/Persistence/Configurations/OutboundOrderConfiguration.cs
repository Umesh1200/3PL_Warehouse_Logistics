using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class OutboundOrderConfiguration : IEntityTypeConfiguration<OutboundOrder>
{
    public void Configure(EntityTypeBuilder<OutboundOrder> builder)
    {
        builder.ToTable("outbound_orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("order_id").ValueGeneratedNever();

        builder.Property(o => o.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(o => o.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(o => o.DestinationFacilityId).HasColumnName("destination_facility_id");
        builder.Property(o => o.CustomerId).HasColumnName("customer_id");

        builder.Property(o => o.OrderNumber).HasColumnName("order_number").HasMaxLength(100).IsRequired();
        builder.Property(o => o.ExternalOrderReference).HasColumnName("external_order_reference").HasMaxLength(100);

        builder.Property(o => o.OrderType).HasColumnName("order_type").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.DeliveryStatus).HasColumnName("delivery_status").HasConversion<string>().HasMaxLength(30).HasDefaultValue(OrderDeliveryStatus.PendingShipment).IsRequired();
        builder.Property(o => o.DeliveryStatusUpdatedAtUtc).HasColumnName("delivery_status_updated_at_utc");
        builder.Property(o => o.DeliveredAtUtc).HasColumnName("delivered_at_utc");
        builder.Property(o => o.StatusBeforeHold).HasColumnName("status_before_hold").HasConversion<string>().HasMaxLength(30);

        builder.Property(o => o.Priority).HasColumnName("priority").IsRequired();
        builder.Property(o => o.ServiceLevel).HasColumnName("service_level").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(o => o.CarrierCode).HasColumnName("carrier_code").HasMaxLength(50);

        builder.Property(o => o.CustomerName).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
        builder.Property(o => o.DestinationAddress).HasColumnName("destination_address").HasMaxLength(500);

        builder.Property(o => o.OrderDateUtc).HasColumnName("order_date_utc").IsRequired();
        builder.Property(o => o.RequiredShipDateUtc).HasColumnName("required_ship_date_utc");
        builder.Property(o => o.EarliestShipDateUtc).HasColumnName("earliest_ship_date_utc");

        builder.Property(o => o.HoldReason).HasColumnName("hold_reason").HasMaxLength(500);
        builder.Property(o => o.HeldAtUtc).HasColumnName("held_at_utc");

        builder.Property(o => o.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
        builder.Property(o => o.CancelledAtUtc).HasColumnName("cancelled_at_utc");

        builder.Property(o => o.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(o => o.AllocationIdempotencyKey).HasColumnName("allocation_idempotency_key").HasMaxLength(100);
        builder.Property(o => o.Version).HasColumnName("version").IsConcurrencyToken();

        // Auditing
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.CreatedBy).HasColumnName("created_by");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        builder.Property(o => o.UpdatedBy).HasColumnName("updated_by");

        // Soft delete
        builder.Property(o => o.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(o => o.DeletedAt).HasColumnName("deleted_at");
        builder.Property(o => o.DeletedBy).HasColumnName("deleted_by");
        builder.HasQueryFilter(o => !o.IsDeleted);

        // Indexes
        builder.HasIndex(o => new { o.FacilityId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.ClientId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.ClientId, o.ExternalOrderReference });
        builder.HasIndex(o => new { o.FacilityId, o.Status, o.Priority });
        builder.HasIndex(o => new { o.ClientId, o.Status });
        builder.HasIndex(o => new { o.ClientId, o.DeliveryStatus });

        // Relationships
        builder.HasOne(o => o.Client).WithMany().HasForeignKey(o => o.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Facility).WithMany().HasForeignKey(o => o.FacilityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.DestinationFacility).WithMany().HasForeignKey(o => o.DestinationFacilityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(o => o.Lines).WithOne(l => l.OutboundOrder).HasForeignKey(l => l.OutboundOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.StatusHistories).WithOne(h => h.OutboundOrder).HasForeignKey(h => h.OutboundOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
