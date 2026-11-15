using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InventorySerialConfiguration : IEntityTypeConfiguration<InventorySerial>
{
    public void Configure(EntityTypeBuilder<InventorySerial> builder)
    {
        builder.ToTable("inventory_serials");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("serial_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(s => s.LocationId)
            .HasColumnName("location_id")
            .IsRequired();

        builder.Property(s => s.LotId)
            .HasColumnName("lot_id");

        builder.Property(s => s.SerialNumber)
            .HasColumnName("serial_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(s => s.ClientId);
        builder.HasIndex(s => s.ItemId);
        builder.HasIndex(s => s.LocationId);
        builder.HasIndex(s => new { s.ItemId, s.SerialNumber }).IsUnique();

        // Relationships
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
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
            .WithMany()
            .HasForeignKey(s => s.LotId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

