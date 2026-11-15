using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CustomerPaymentConfiguration : IEntityTypeConfiguration<CustomerPayment>
{
    public void Configure(EntityTypeBuilder<CustomerPayment> builder)
    {
        builder.ToTable("customer_payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("payment_id")
            .ValueGeneratedNever();

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(p => p.PaymentReference)
            .HasColumnName("payment_reference")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.PaymentDate)
            .HasColumnName("payment_date")
            .IsRequired();

        builder.Property(p => p.PaymentMethod)
            .HasColumnName("payment_method")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(p => p.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.AllocatedAmount)
            .HasColumnName("allocated_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.UnallocatedAmount)
            .HasColumnName("unallocated_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.IsReversed)
            .HasColumnName("is_reversed")
            .IsRequired();

        builder.Property(p => p.ReversedAtUtc)
            .HasColumnName("reversed_at_utc");

        builder.Property(p => p.ReversalReason)
            .HasColumnName("reversal_reason")
            .HasMaxLength(500);

        builder.Property(p => p.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(p => new { p.ClientId, p.PaymentDate })
            .HasDatabaseName("ix_customer_payments_client_date");

        builder.HasIndex(p => new { p.ClientId, p.PaymentReference })
            .HasDatabaseName("ix_customer_payments_client_ref");

        // Relationships
        builder.HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Allocations)
            .WithOne(a => a.CustomerPayment)
            .HasForeignKey(a => a.CustomerPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

