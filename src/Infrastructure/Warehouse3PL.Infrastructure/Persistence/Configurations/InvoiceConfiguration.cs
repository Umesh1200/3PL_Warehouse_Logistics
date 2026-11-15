using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasColumnName("invoice_id")
            .ValueGeneratedNever();

        builder.Property(i => i.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(i => i.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(i => i.BillingPeriodId)
            .HasColumnName("billing_period_id")
            .IsRequired();

        builder.Property(i => i.InvoiceNumber)
            .HasColumnName("invoice_number")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(i => i.InvoiceDate)
            .HasColumnName("invoice_date")
            .IsRequired();

        builder.Property(i => i.DueDate)
            .HasColumnName("due_date")
            .IsRequired();

        builder.Property(i => i.PaymentTermsDays)
            .HasColumnName("payment_terms_days")
            .IsRequired();

        builder.Property(i => i.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(i => i.SubtotalAmount)
            .HasColumnName("subtotal_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.TaxRate)
            .HasColumnName("tax_rate")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.TaxAmount)
            .HasColumnName("tax_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.PaidAmount)
            .HasColumnName("paid_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.CreditedAmount)
            .HasColumnName("credited_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.OutstandingBalance)
            .HasColumnName("outstanding_balance")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.ApprovedAtUtc)
            .HasColumnName("approved_at_utc");

        builder.Property(i => i.ApprovedByUserId)
            .HasColumnName("approved_by_user_id");

        builder.Property(i => i.IssuedAtUtc)
            .HasColumnName("issued_at_utc");

        builder.Property(i => i.IssuedByUserId)
            .HasColumnName("issued_by_user_id");

        builder.Property(i => i.VoidedAtUtc)
            .HasColumnName("voided_at_utc");

        builder.Property(i => i.VoidedByUserId)
            .HasColumnName("voided_by_user_id");

        builder.Property(i => i.VoidReason)
            .HasColumnName("void_reason")
            .HasMaxLength(500);

        builder.Property(i => i.ReplacementInvoiceId)
            .HasColumnName("replacement_invoice_id");

        builder.Property(i => i.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.CreatedBy).HasColumnName("created_by");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Property(i => i.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(i => new { i.ClientId, i.InvoiceNumber })
            .HasDatabaseName("ix_invoices_client_number")
            .IsUnique();

        builder.HasIndex(i => new { i.ClientId, i.BillingPeriodId, i.Currency })
            .HasDatabaseName("ix_invoices_client_period_currency");

        builder.HasIndex(i => new { i.ClientId, i.Status, i.DueDate })
            .HasDatabaseName("ix_invoices_client_status_due");

        // Relationships
        builder.HasOne(i => i.Client)
            .WithMany()
            .HasForeignKey(i => i.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Facility)
            .WithMany()
            .HasForeignKey(i => i.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.BillingPeriod)
            .WithMany()
            .HasForeignKey(i => i.BillingPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ReplacementInvoice)
            .WithMany()
            .HasForeignKey(i => i.ReplacementInvoiceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(i => i.Lines)
            .WithOne(l => l.Invoice)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.PaymentAllocations)
            .WithOne(a => a.Invoice)
            .HasForeignKey(a => a.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Disputes)
            .WithOne(d => d.Invoice)
            .HasForeignKey(d => d.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.CreditNotes)
            .WithOne(c => c.OriginalInvoice)
            .HasForeignKey(c => c.OriginalInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

