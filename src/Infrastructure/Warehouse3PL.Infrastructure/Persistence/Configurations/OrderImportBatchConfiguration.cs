using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class OrderImportBatchConfiguration : IEntityTypeConfiguration<OrderImportBatch>
{
    public void Configure(EntityTypeBuilder<OrderImportBatch> builder)
    {
        builder.ToTable("order_import_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("batch_id").ValueGeneratedNever();

        builder.Property(b => b.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(b => b.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(b => b.BatchNumber).HasColumnName("batch_number").HasMaxLength(50).IsRequired();
        builder.Property(b => b.SourceFileName).HasColumnName("source_file_name").HasMaxLength(255).IsRequired();

        builder.Property(b => b.TotalRows).HasColumnName("total_rows").IsRequired();
        builder.Property(b => b.SuccessfulRows).HasColumnName("successful_rows").IsRequired();
        builder.Property(b => b.FailedRows).HasColumnName("failed_rows").IsRequired();
        builder.Property(b => b.DuplicateRows).HasColumnName("duplicate_rows").IsRequired();

        builder.Property(b => b.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(b => b.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(b => b.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(b => b.ErrorDetailsJson).HasColumnName("error_details_json");

        // Auditing
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.CreatedBy).HasColumnName("created_by");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");
        builder.Property(b => b.UpdatedBy).HasColumnName("updated_by");

        // Index
        builder.HasIndex(b => new { b.ClientId, b.BatchNumber }).IsUnique();
        builder.HasIndex(b => new { b.FacilityId, b.Status });

        // Relationships
        builder.HasOne(b => b.Client).WithMany().HasForeignKey(b => b.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Facility).WithMany().HasForeignKey(b => b.FacilityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Rows).WithOne(r => r.OrderImportBatch).HasForeignKey(r => r.OrderImportBatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

