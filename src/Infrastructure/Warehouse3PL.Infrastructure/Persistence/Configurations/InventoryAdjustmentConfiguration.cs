using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.ToTable("inventory_adjustments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("adj_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.StockId)
            .HasColumnName("stock_id")
            .IsRequired();

        builder.Property(a => a.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(a => a.LocationId)
            .HasColumnName("location_id")
            .IsRequired();

        builder.Property(a => a.LotId)
            .HasColumnName("lot_id");

        builder.Property(a => a.QtyAdjusted)
            .HasColumnName("qty_adjusted")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(a => a.ReasonCode)
            .HasColumnName("reason_code")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.ReasonNotes)
            .HasColumnName("reason_notes")
            .HasMaxLength(500);

        builder.Property(a => a.AccountingImpactAmount)
            .HasColumnName("accounting_impact")
            .HasPrecision(14, 4);

        builder.Property(a => a.ApprovedByUserId)
            .HasColumnName("approved_by_user");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // Indexes
        builder.HasIndex(a => a.ClientId);
        builder.HasIndex(a => a.FacilityId);
        builder.HasIndex(a => a.StockId);
        builder.HasIndex(a => a.ItemId);

        // Relationships
        builder.HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Facility)
            .WithMany()
            .HasForeignKey(a => a.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Stock)
            .WithMany()
            .HasForeignKey(a => a.StockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Item)
            .WithMany()
            .HasForeignKey(a => a.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Location)
            .WithMany()
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Lot)
            .WithMany()
            .HasForeignKey(a => a.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.ApprovedByUser)
            .WithMany()
            .HasForeignKey(a => a.ApprovedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

