using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ProjectionCheckpointConfiguration : IEntityTypeConfiguration<ProjectionCheckpoint>
{
    public void Configure(EntityTypeBuilder<ProjectionCheckpoint> builder)
    {
        builder.ToTable("projection_checkpoints");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("checkpoint_id")
            .ValueGeneratedNever();

        builder.Property(c => c.ProjectionName)
            .HasColumnName("projection_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(c => c.SourceStreamId)
            .HasColumnName("source_stream_id")
            .HasMaxLength(150)
            .HasDefaultValue("default")
            .IsRequired();

        builder.Property(c => c.LastProcessedEventId)
            .HasColumnName("last_processed_event_id");

        builder.Property(c => c.LastProcessedSequenceNumber)
            .HasColumnName("last_processed_sequence_number")
            .IsRequired();

        builder.Property(c => c.LastProcessedTimestampUtc)
            .HasColumnName("last_processed_timestamp_utc")
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(c => c.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2000);

        builder.Property(c => c.ProcessedCount)
            .HasColumnName("processed_count")
            .IsRequired();

        // Auditing
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee: Unique checkpoint per (projection_name, source_stream_id)
        builder.HasIndex(c => new { c.ProjectionName, c.SourceStreamId })
            .HasDatabaseName("ix_projection_checkpoints_projection_stream")
            .IsUnique();
    }
}

