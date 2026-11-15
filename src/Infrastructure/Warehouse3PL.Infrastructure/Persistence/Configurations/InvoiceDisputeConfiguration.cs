using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InvoiceDisputeConfiguration : IEntityTypeConfiguration<InvoiceDispute>
{
    public void Configure(EntityTypeBuilder<InvoiceDispute> builder)
    {
        builder.ToTable("invoice_disputes");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("dispute_id")
            .ValueGeneratedNever();

        builder.Property(d => d.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(d => d.InvoiceId)
            .HasColumnName("invoice_id")
            .IsRequired();

        builder.Property(d => d.InvoiceLineId)
            .HasColumnName("invoice_line_id");

        builder.Property(d => d.DisputeReference)
            .HasColumnName("dispute_reference")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.DisputedAmount)
            .HasColumnName("disputed_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(d => d.ReasonCode)
            .HasColumnName("reason_code")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(d => d.ReasonDescription)
            .HasColumnName("reason_description")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(d => d.SubmittedAtUtc)
            .HasColumnName("submitted_at_utc")
            .IsRequired();

        builder.Property(d => d.SubmittedByUserId)
            .HasColumnName("submitted_by_user_id");

        builder.Property(d => d.ReviewedAtUtc)
            .HasColumnName("reviewed_at_utc");

        builder.Property(d => d.ReviewedByUserId)
            .HasColumnName("reviewed_by_user_id");

        builder.Property(d => d.ReviewNotes)
            .HasColumnName("review_notes")
            .HasMaxLength(1000);

        builder.Property(d => d.ResolvedAtUtc)
            .HasColumnName("resolved_at_utc");

        builder.Property(d => d.ResolvedByUserId)
            .HasColumnName("resolved_by_user_id");

        builder.Property(d => d.ResolutionNotes)
            .HasColumnName("resolution_notes")
            .HasMaxLength(1000);

        builder.Property(d => d.ApprovedCreditAmount)
            .HasColumnName("approved_credit_amount")
            .HasPrecision(18, 4);

        builder.Property(d => d.CreditNoteId)
            .HasColumnName("credit_note_id");

        // Auditing
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.CreatedBy).HasColumnName("created_by");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        builder.Property(d => d.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(d => new { d.ClientId, d.DisputeReference })
            .HasDatabaseName("ix_invoice_disputes_client_ref")
            .IsUnique();

        builder.HasIndex(d => new { d.InvoiceId, d.Status })
            .HasDatabaseName("ix_invoice_disputes_invoice_status");

        // Relationships
        builder.HasOne(d => d.Client)
            .WithMany()
            .HasForeignKey(d => d.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Invoice)
            .WithMany(i => i.Disputes)
            .HasForeignKey(d => d.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.InvoiceLine)
            .WithMany()
            .HasForeignKey(d => d.InvoiceLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.CreditNote)
            .WithMany()
            .HasForeignKey(d => d.CreditNoteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

