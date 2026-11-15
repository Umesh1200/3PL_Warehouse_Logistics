using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("inventory_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("tx_id")
            .ValueGeneratedOnAdd(); // BIGINT auto-incrementing sequence

        builder.Property(t => t.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(t => t.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(t => t.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(t => t.LotId)
            .HasColumnName("lot_id");

        builder.Property(t => t.FromLocationId)
            .HasColumnName("from_location_id");

        builder.Property(t => t.ToLocationId)
            .HasColumnName("to_location_id");

        builder.Property(t => t.TxType)
            .HasColumnName("tx_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.Quantity)
            .HasColumnName("qty")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(t => t.ReferenceType)
            .HasColumnName("reference_type")
            .HasMaxLength(50);

        builder.Property(t => t.ReferenceId)
            .HasColumnName("reference_id");

        builder.Property(t => t.UserId)
            .HasColumnName("user_id");

        builder.Property(t => t.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(t => t.TxTimestamp)
            .HasColumnName("tx_timestamp")
            .IsRequired();

        // High-performance audit indexes
        builder.HasIndex(t => new { t.ClientId, t.TxTimestamp });
        builder.HasIndex(t => t.FacilityId);
        builder.HasIndex(t => t.ItemId);
        builder.HasIndex(t => new { t.FromLocationId, t.ToLocationId });
        builder.HasIndex(t => t.ReferenceId);

        // Relationships
        builder.HasOne(t => t.Client)
            .WithMany()
            .HasForeignKey(t => t.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Facility)
            .WithMany()
            .HasForeignKey(t => t.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Item)
            .WithMany()
            .HasForeignKey(t => t.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Lot)
            .WithMany()
            .HasForeignKey(t => t.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.FromLocation)
            .WithMany()
            .HasForeignKey(t => t.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ToLocation)
            .WithMany()
            .HasForeignKey(t => t.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

