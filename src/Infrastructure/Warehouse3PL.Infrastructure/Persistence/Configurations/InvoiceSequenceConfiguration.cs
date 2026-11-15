using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.ToTable("invoice_sequences");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("sequence_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FiscalYear)
            .HasColumnName("fiscal_year")
            .IsRequired();

        builder.Property(s => s.Prefix)
            .HasColumnName("prefix")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(s => s.LastSequenceNumber)
            .HasColumnName("last_sequence_number")
            .IsRequired();

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Indexes & Constraints
        builder.HasIndex(s => new { s.ClientId, s.FiscalYear, s.Prefix })
            .HasDatabaseName("ix_invoice_sequences_client_year_prefix")
            .IsUnique();

        // Relationships
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

