using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    public void Configure(EntityTypeBuilder<CreditNoteLine> builder)
    {
        builder.ToTable("credit_note_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("credit_note_line_id")
            .ValueGeneratedNever();

        builder.Property(l => l.CreditNoteId)
            .HasColumnName("credit_note_id")
            .IsRequired();

        builder.Property(l => l.OriginalInvoiceLineId)
            .HasColumnName("original_invoice_line_id");

        builder.Property(l => l.LineNumber)
            .HasColumnName("line_number")
            .IsRequired();

        builder.Property(l => l.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(l => l.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        // Auditing
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.CreatedBy).HasColumnName("created_by");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(l => new { l.CreditNoteId, l.LineNumber })
            .HasDatabaseName("ix_credit_note_lines_number")
            .IsUnique();

        // Relationships
        builder.HasOne(l => l.CreditNote)
            .WithMany(c => c.Lines)
            .HasForeignKey(l => l.CreditNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.OriginalInvoiceLine)
            .WithMany()
            .HasForeignKey(l => l.OriginalInvoiceLineId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

