using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReceivingDiscrepancyConfiguration : IEntityTypeConfiguration<ReceivingDiscrepancy>
{
    public void Configure(EntityTypeBuilder<ReceivingDiscrepancy> builder)
    {
        builder.ToTable("receiving_discrepancies");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("discrepancy_id")
            .ValueGeneratedNever();

        builder.Property(d => d.ReceivingSessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(d => d.ReceiptLineId)
            .HasColumnName("receipt_line_id");

        builder.Property(d => d.Type)
            .HasColumnName("discrepancy_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(d => d.DiscrepantQuantity)
            .HasColumnName("discrepant_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(d => d.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(d => d.ReportedAtUtc)
            .HasColumnName("reported_at_utc")
            .IsRequired();

        builder.Property(d => d.ResolvedAtUtc)
            .HasColumnName("resolved_at_utc");

        builder.Property(d => d.ResolvedByUserId)
            .HasColumnName("resolved_by_user_id");

        builder.Property(d => d.ResolutionNotes)
            .HasColumnName("resolution_notes")
            .HasMaxLength(1000);

        // Optimistic concurrency token
        builder.Property(d => d.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(d => d.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(d => d.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(d => d.DeletedBy)
            .HasColumnName("deleted_by");

        // Relationships
        builder.HasOne(d => d.ReceivingSession)
            .WithMany(s => s.Discrepancies)
            .HasForeignKey(d => d.ReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.ReceiptLine)
            .WithMany(l => l.Discrepancies)
            .HasForeignKey(d => d.ReceiptLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.ResolvedByUser)
            .WithMany()
            .HasForeignKey(d => d.ResolvedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

