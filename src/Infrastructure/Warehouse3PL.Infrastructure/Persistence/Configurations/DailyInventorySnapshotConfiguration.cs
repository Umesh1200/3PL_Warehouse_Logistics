using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyInventorySnapshotConfiguration : IEntityTypeConfiguration<DailyInventorySnapshot>
{
    public void Configure(EntityTypeBuilder<DailyInventorySnapshot> builder)
    {
        builder.ToTable("daily_inventory_snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("snapshot_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.SnapshotDate)
            .HasColumnName("snapshot_date")
            .IsRequired();

        builder.Property(s => s.SnapshotTimestampUtc)
            .HasColumnName("snapshot_timestamp_utc")
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.TotalQuantity)
            .HasColumnName("total_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.TotalBillableQuantity)
            .HasColumnName("total_billable_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.TotalPalletCount)
            .HasColumnName("total_pallet_count")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.TotalBillablePalletCount)
            .HasColumnName("total_billable_pallet_count")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.TotalCartonCount)
            .HasColumnName("total_carton_count")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.TotalBillableCartonCount)
            .HasColumnName("total_billable_carton_count")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.TotalCbm)
            .HasColumnName("total_cbm")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.TotalBillableCbm)
            .HasColumnName("total_billable_cbm")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.RecordCount)
            .HasColumnName("record_count")
            .IsRequired();

        builder.Property(s => s.HasMissingDimensionsRecords)
            .HasColumnName("has_missing_dimensions_records")
            .IsRequired();

        builder.Property(s => s.FinalizedAtUtc)
            .HasColumnName("finalized_at_utc");

        builder.Property(s => s.FinalizedByUserId)
            .HasColumnName("finalized_by_user_id");

        builder.Property(s => s.RatedAtUtc)
            .HasColumnName("rated_at_utc");

        builder.Property(s => s.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee: Exactly one snapshot per Client, Facility, and Date
        builder.HasIndex(s => new { s.ClientId, s.FacilityId, s.SnapshotDate })
            .HasDatabaseName("ix_daily_inventory_snapshots_client_facility_date")
            .IsUnique();

        builder.HasIndex(s => new { s.ClientId, s.Status })
            .HasDatabaseName("ix_daily_inventory_snapshots_client_status");

        // Relationships
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Records)
            .WithOne(r => r.DailyInventorySnapshot)
            .HasForeignKey(r => r.DailyInventorySnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

