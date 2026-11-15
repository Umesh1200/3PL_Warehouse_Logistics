using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("payment_allocations");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("allocation_id")
            .ValueGeneratedNever();

        builder.Property(a => a.CustomerPaymentId)
            .HasColumnName("customer_payment_id")
            .IsRequired();

        builder.Property(a => a.InvoiceId)
            .HasColumnName("invoice_id")
            .IsRequired();

        builder.Property(a => a.AllocatedAmount)
            .HasColumnName("allocated_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.AllocatedAtUtc)
            .HasColumnName("allocated_at_utc")
            .IsRequired();

        builder.Property(a => a.IsReversed)
            .HasColumnName("is_reversed")
            .IsRequired();

        builder.Property(a => a.ReversedAtUtc)
            .HasColumnName("reversed_at_utc");

        builder.Property(a => a.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        // Auditing
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(a => new { a.CustomerPaymentId, a.InvoiceId })
            .HasDatabaseName("ix_payment_allocations_payment_invoice");

        builder.HasIndex(a => a.InvoiceId)
            .HasDatabaseName("ix_payment_allocations_invoice");

        // Relationships
        builder.HasOne(a => a.CustomerPayment)
            .WithMany(p => p.Allocations)
            .HasForeignKey(a => a.CustomerPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Invoice)
            .WithMany(i => i.PaymentAllocations)
            .HasForeignKey(a => a.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

