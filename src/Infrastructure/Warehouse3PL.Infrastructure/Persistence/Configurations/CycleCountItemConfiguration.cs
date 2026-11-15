using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CycleCountItemConfiguration : IEntityTypeConfiguration<CycleCountItem>
{
    public void Configure(EntityTypeBuilder<CycleCountItem> builder)
    {
        builder.ToTable("cycle_count_items");

        builder.HasKey(cci => cci.Id);
        builder.Property(cci => cci.Id)
            .HasColumnName("count_item_id")
            .ValueGeneratedNever();

        builder.Property(cci => cci.CycleCountId)
            .HasColumnName("count_id")
            .IsRequired();

        builder.Property(cci => cci.LocationId)
            .HasColumnName("location_id")
            .IsRequired();

        builder.Property(cci => cci.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(cci => cci.LotId)
            .HasColumnName("lot_id");

        builder.Property(cci => cci.ExpectedQty)
            .HasColumnName("expected_qty")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(cci => cci.CountedQty)
            .HasColumnName("counted_qty")
            .HasPrecision(14, 4);

        builder.Property(cci => cci.CounterUserId)
            .HasColumnName("counter_user_id");

        builder.Property(cci => cci.CountedAt)
            .HasColumnName("counted_at");

        builder.Property(cci => cci.ApprovedByUserId)
            .HasColumnName("approved_by");

        builder.Property(cci => cci.ApprovedAt)
            .HasColumnName("approved_at");

        builder.Property(cci => cci.IsReconciled)
            .HasColumnName("is_reconciled")
            .HasDefaultValue(false);

        // Indexes
        builder.HasIndex(cci => cci.CycleCountId);
        builder.HasIndex(cci => cci.LocationId);
        builder.HasIndex(cci => cci.ItemId);

        // Relationships
        builder.HasOne(cci => cci.CycleCount)
            .WithMany(cc => cc.Items)
            .HasForeignKey(cci => cci.CycleCountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cci => cci.Location)
            .WithMany()
            .HasForeignKey(cci => cci.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cci => cci.Item)
            .WithMany()
            .HasForeignKey(cci => cci.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cci => cci.Lot)
            .WithMany()
            .HasForeignKey(cci => cci.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(cci => cci.CounterUser)
            .WithMany()
            .HasForeignKey(cci => cci.CounterUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(cci => cci.ApprovedByUser)
            .WithMany()
            .HasForeignKey(cci => cci.ApprovedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

