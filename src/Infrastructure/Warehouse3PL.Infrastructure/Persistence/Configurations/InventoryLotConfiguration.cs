using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
{
    public void Configure(EntityTypeBuilder<InventoryLot> builder)
    {
        builder.ToTable("inventory_lots");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("lot_id")
            .ValueGeneratedNever();

        builder.Property(l => l.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(l => l.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(l => l.LotNumber)
            .HasColumnName("lot_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(l => l.ManufactureDate)
            .HasColumnName("mfg_date");

        builder.Property(l => l.ExpirationDate)
            .HasColumnName("expiry_date");

        builder.Property(l => l.QaStatus)
            .HasColumnName("qa_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(l => l.QaNotes)
            .HasColumnName("qa_notes")
            .HasMaxLength(500);

        // Auditing
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.CreatedBy).HasColumnName("created_by");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(l => l.ClientId);
        builder.HasIndex(l => l.ItemId);
        builder.HasIndex(l => new { l.ItemId, l.LotNumber }).IsUnique();
        builder.HasIndex(l => l.ExpirationDate);

        // Relationships
        builder.HasOne(l => l.Client)
            .WithMany()
            .HasForeignKey(l => l.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Item)
            .WithMany()
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

