using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReturnReceivingDiscrepancyConfiguration : IEntityTypeConfiguration<ReturnReceivingDiscrepancy>
{
    public void Configure(EntityTypeBuilder<ReturnReceivingDiscrepancy> builder)
    {
        builder.ToTable("return_receiving_discrepancies");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("discrepancy_id")
            .ValueGeneratedNever();

        builder.Property(d => d.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(d => d.ReturnReceivingSessionId)
            .HasColumnName("return_receiving_session_id")
            .IsRequired();

        builder.Property(d => d.ItemId)
            .HasColumnName("item_id");

        builder.Property(d => d.ExpectedSku)
            .HasColumnName("expected_sku")
            .HasMaxLength(100);

        builder.Property(d => d.ActualSku)
            .HasColumnName("actual_sku")
            .HasMaxLength(100);

        builder.Property(d => d.DiscrepancyType)
            .HasColumnName("discrepancy_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(d => d.ExpectedQuantity)
            .HasColumnName("expected_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(d => d.ReceivedQuantity)
            .HasColumnName("received_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(d => d.DiscrepancyQuantity)
            .HasColumnName("discrepancy_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(d => d.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(d => d.ReportedAtUtc)
            .HasColumnName("reported_at_utc")
            .IsRequired();

        // Indexes
        builder.HasIndex(d => new { d.ReturnReceivingSessionId, d.DiscrepancyType })
            .HasDatabaseName("ix_return_receiving_discrepancies_session_type");

        // Relationships
        builder.HasOne(d => d.ReturnReceivingSession)
            .WithMany(s => s.Discrepancies)
            .HasForeignKey(d => d.ReturnReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Item)
            .WithMany()
            .HasForeignKey(d => d.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

