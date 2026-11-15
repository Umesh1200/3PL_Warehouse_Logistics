using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines");

        builder.HasKey(pol => pol.Id);
        builder.Property(pol => pol.Id)
            .HasColumnName("po_line_id")
            .ValueGeneratedNever();

        builder.Property(pol => pol.PurchaseOrderId)
            .HasColumnName("purchase_order_id")
            .IsRequired();

        builder.Property(pol => pol.LineNumber)
            .HasColumnName("line_number")
            .IsRequired();

        // Enforce unique line number per purchase order
        builder.HasIndex(pol => new { pol.PurchaseOrderId, pol.LineNumber })
            .IsUnique();

        builder.Property(pol => pol.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(pol => pol.ExpectedQty)
            .HasColumnName("expected_qty")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(pol => pol.ReceivedQty)
            .HasColumnName("received_qty")
            .HasPrecision(14, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(pol => pol.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(14, 4);

        builder.Property(pol => pol.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .HasDefaultValue("USD");

        builder.Property(pol => pol.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(pol => pol.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        // Relationships
        builder.HasOne(pol => pol.Item)
            .WithMany()
            .HasForeignKey(pol => pol.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
