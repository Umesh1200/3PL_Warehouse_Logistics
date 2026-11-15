using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class OrderImportBatchRowConfiguration : IEntityTypeConfiguration<OrderImportBatchRow>
{
    public void Configure(EntityTypeBuilder<OrderImportBatchRow> builder)
    {
        builder.ToTable("order_import_batch_rows");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("row_id").ValueGeneratedNever();

        builder.Property(r => r.OrderImportBatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(r => r.RowNumber).HasColumnName("row_number").IsRequired();
        builder.Property(r => r.ExternalOrderReference).HasColumnName("external_order_reference").HasMaxLength(100);
        builder.Property(r => r.IsSuccess).HasColumnName("is_success").IsRequired();
        builder.Property(r => r.IsDuplicate).HasColumnName("is_duplicate").IsRequired();
        builder.Property(r => r.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
        builder.Property(r => r.CreatedOutboundOrderId).HasColumnName("created_order_id");

        // Index
        builder.HasIndex(r => new { r.OrderImportBatchId, r.RowNumber });

        // Relationship
        builder.HasOne(r => r.OrderImportBatch)
            .WithMany(b => b.Rows)
            .HasForeignKey(r => r.OrderImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

