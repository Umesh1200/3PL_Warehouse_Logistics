using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyInventorySnapshotRecordConfiguration : IEntityTypeConfiguration<DailyInventorySnapshotRecord>
{
    public void Configure(EntityTypeBuilder<DailyInventorySnapshotRecord> builder)
    {
        builder.ToTable("daily_inventory_snapshot_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("record_id")
            .ValueGeneratedNever();

        builder.Property(r => r.DailyInventorySnapshotId)
            .HasColumnName("snapshot_id")
            .IsRequired();

        builder.Property(r => r.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(r => r.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(r => r.LocationId)
            .HasColumnName("location_id")
            .IsRequired();

        builder.Property(r => r.LocationCode)
            .HasColumnName("location_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(r => r.Sku)
            .HasColumnName("sku")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.LotId)
            .HasColumnName("lot_id");

        builder.Property(r => r.LotNumber)
            .HasColumnName("lot_number")
            .HasMaxLength(100);

        builder.Property(r => r.HandlingUnitId)
            .HasColumnName("handling_unit_id");

        builder.Property(r => r.LpnCode)
            .HasColumnName("lpn_code")
            .HasMaxLength(100);

        builder.Property(r => r.HoldingStatus)
            .HasColumnName("holding_status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(r => r.PalletCount)
            .HasColumnName("pallet_count")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(r => r.CartonCount)
            .HasColumnName("carton_count")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(r => r.Cbm)
            .HasColumnName("cbm")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(r => r.HasMissingDimensions)
            .HasColumnName("has_missing_dimensions")
            .IsRequired();

        builder.Property(r => r.IsBillable)
            .HasColumnName("is_billable")
            .IsRequired();

        builder.Property(r => r.SourceInventoryReference)
            .HasColumnName("source_inventory_reference")
            .HasMaxLength(100)
            .IsRequired();

        // Auditing
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(r => new { r.DailyInventorySnapshotId, r.IsBillable })
            .HasDatabaseName("ix_snapshot_records_snapshot_billable");

        builder.HasIndex(r => new { r.ClientId, r.FacilityId, r.ItemId })
            .HasDatabaseName("ix_snapshot_records_client_facility_item");

        // Relationships
        builder.HasOne(r => r.DailyInventorySnapshot)
            .WithMany(s => s.Records)
            .HasForeignKey(r => r.DailyInventorySnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Location)
            .WithMany()
            .HasForeignKey(r => r.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Item)
            .WithMany()
            .HasForeignKey(r => r.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.HandlingUnit)
            .WithMany()
            .HasForeignKey(r => r.HandlingUnitId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

