using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("credit_notes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("credit_note_id")
            .ValueGeneratedNever();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(c => c.OriginalInvoiceId)
            .HasColumnName("original_invoice_id")
            .IsRequired();

        builder.Property(c => c.DisputeId)
            .HasColumnName("dispute_id");

        builder.Property(c => c.CreditNoteNumber)
            .HasColumnName("credit_note_number")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.IssueDate)
            .HasColumnName("issue_date")
            .IsRequired();

        builder.Property(c => c.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(c => c.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(c => c.AllocatedAmount)
            .HasColumnName("allocated_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(c => c.UnallocatedAmount)
            .HasColumnName("unallocated_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(c => c.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(c => c.VoidReason)
            .HasColumnName("void_reason")
            .HasMaxLength(500);

        // Auditing
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(c => new { c.ClientId, c.CreditNoteNumber })
            .HasDatabaseName("ix_credit_notes_client_number")
            .IsUnique();

        builder.HasIndex(c => c.OriginalInvoiceId)
            .HasDatabaseName("ix_credit_notes_invoice");

        // Relationships
        builder.HasOne(c => c.Client)
            .WithMany()
            .HasForeignKey(c => c.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.OriginalInvoice)
            .WithMany(i => i.CreditNotes)
            .HasForeignKey(c => c.OriginalInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Dispute)
            .WithMany()
            .HasForeignKey(c => c.DisputeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.Lines)
            .WithOne(l => l.CreditNote)
            .HasForeignKey(l => l.CreditNoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

