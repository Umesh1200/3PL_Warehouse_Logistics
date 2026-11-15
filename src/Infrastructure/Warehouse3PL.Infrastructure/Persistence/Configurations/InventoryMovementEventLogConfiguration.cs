using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InventoryMovementEventLogConfiguration : IEntityTypeConfiguration<InventoryMovementEventLog>
{
    public void Configure(EntityTypeBuilder<InventoryMovementEventLog> builder)
    {
        builder.ToTable("inventory_movement_event_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("log_id")
            .ValueGeneratedNever();

        builder.Property(l => l.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(l => l.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(l => l.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(l => l.ItemId).HasColumnName("item_id").IsRequired();
        builder.Property(l => l.Sku).HasColumnName("sku").HasMaxLength(100).IsRequired();

        builder.Property(l => l.EventType)
            .HasColumnName("event_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.IsQualifyingDemand).HasColumnName("is_qualifying_demand").IsRequired();
        builder.Property(l => l.IsPhysicalMovement).HasColumnName("is_physical_movement").IsRequired();
        builder.Property(l => l.IsCanonicalDemand).HasColumnName("is_canonical_demand").IsRequired();

        builder.Property(l => l.Quantity).HasColumnName("quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(l => l.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(l => l.TotalCostValue).HasColumnName("total_cost_value").HasPrecision(18, 4).IsRequired();
        builder.Property(l => l.TimestampUtc).HasColumnName("timestamp_utc").IsRequired();

        builder.Property(l => l.SourceLocationId).HasColumnName("source_location_id").IsRequired(false);
        builder.Property(l => l.DestinationLocationId).HasColumnName("destination_location_id").IsRequired(false);
        builder.Property(l => l.ZoneId).HasColumnName("zone_id").IsRequired(false);
        builder.Property(l => l.ReferenceNumber).HasColumnName("reference_number").HasMaxLength(100).IsRequired();
        builder.Property(l => l.SequenceNumber).HasColumnName("sequence_number").HasDefaultValue(0L);

        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(l => l.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(l => new { l.FacilityId, l.ClientId, l.ItemId, l.TimestampUtc })
            .HasDatabaseName("ix_inv_movement_logs_facility_client_item_timestamp");

        builder.HasIndex(l => new { l.FacilityId, l.IsQualifyingDemand, l.TimestampUtc })
            .HasDatabaseName("ix_inv_movement_logs_facility_demand_timestamp");
    }
}

