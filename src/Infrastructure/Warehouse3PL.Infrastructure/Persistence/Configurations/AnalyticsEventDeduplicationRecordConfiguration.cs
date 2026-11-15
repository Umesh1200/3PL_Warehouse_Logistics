using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class AnalyticsEventDeduplicationRecordConfiguration : IEntityTypeConfiguration<AnalyticsEventDeduplicationRecord>
{
    public void Configure(EntityTypeBuilder<AnalyticsEventDeduplicationRecord> builder)
    {
        builder.ToTable("analytics_event_deduplications");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("deduplication_id")
            .ValueGeneratedNever();

        builder.Property(d => d.ProjectionName)
            .HasColumnName("projection_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(d => d.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(d => d.EventName)
            .HasColumnName("event_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(d => d.ProcessedAtUtc)
            .HasColumnName("processed_at_utc")
            .IsRequired();

        // Auditing
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.CreatedBy).HasColumnName("created_by");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        builder.Property(d => d.UpdatedBy).HasColumnName("updated_by");

        // Database Uniqueness Guarantee: Prevent double-counting of same event within a projection
        builder.HasIndex(d => new { d.ProjectionName, d.EventId })
            .HasDatabaseName("ix_analytics_event_deduplications_proj_event")
            .IsUnique();
    }
}

