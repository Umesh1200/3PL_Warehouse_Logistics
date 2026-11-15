using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class StockOnHandConfiguration : IEntityTypeConfiguration<StockOnHand>
{
    public void Configure(EntityTypeBuilder<StockOnHand> builder)
    {
        builder.ToTable("stock_on_hand");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("stock_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(s => s.LocationId)
            .HasColumnName("location_id")
            .IsRequired();

        builder.Property(s => s.LotId)
            .HasColumnName("lot_id");

        builder.Property(s => s.HandlingUnitId)
            .HasColumnName("handling_unit_id");

        builder.Property(s => s.LpnCode)
            .HasColumnName("lpn_code")
            .HasMaxLength(100);

        builder.Property(s => s.QtyAvailable)
            .HasColumnName("qty_available")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m);

        builder.Property(s => s.QtyAllocated)
            .HasColumnName("qty_allocated")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m);

        builder.Property(s => s.QtyReserved)
            .HasColumnName("qty_reserved")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m);

        builder.Property(s => s.QtyQuarantined)
            .HasColumnName("qty_quarantined")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m);

        builder.Property(s => s.QtyDamaged)
            .HasColumnName("qty_damaged")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m);

        builder.Property(s => s.QtyOnHold)
            .HasColumnName("qty_on_hold")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m);

        builder.Property(s => s.LastCountedAt)
            .HasColumnName("last_counted_at");

        builder.Property(s => s.ReceivedAtUtc)
            .HasColumnName("received_at_utc");

        // Concurrency token for optimistic locking
        builder.Property(s => s.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1u);

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Multi-column Indexes
        builder.HasIndex(s => s.ClientId);
        builder.HasIndex(s => s.FacilityId);
        builder.HasIndex(s => s.ItemId);
        builder.HasIndex(s => s.LocationId);
        builder.HasIndex(s => s.ReceivedAtUtc);
        builder.HasIndex(s => new { s.ClientId, s.FacilityId, s.ItemId, s.LocationId, s.LotId })
            .IsUnique();

        // Relationships
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Item)
            .WithMany()
            .HasForeignKey(s => s.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Location)
            .WithMany()
            .HasForeignKey(s => s.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Lot)
            .WithMany(l => l.StockRecords)
            .HasForeignKey(s => s.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.HandlingUnit)
            .WithMany()
            .HasForeignKey(s => s.HandlingUnitId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

