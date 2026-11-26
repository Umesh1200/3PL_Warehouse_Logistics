using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ConfigurationInvalidationEventConfiguration : IEntityTypeConfiguration<ConfigurationInvalidationEvent>
{
    public void Configure(EntityTypeBuilder<ConfigurationInvalidationEvent> builder)
    {
        builder.ToTable("configuration_invalidation_events", t =>
        {
            t.HasCheckConstraint("ck_configuration_invalidation_events_epoch_positive", "new_epoch >= 1");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("event_id").ValueGeneratedOnAdd();

        builder.Property(e => e.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.ScopeKey)
            .HasColumnName("scope_key")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(e => e.NewEpoch)
            .HasColumnName("new_epoch")
            .IsRequired();

        builder.Property(e => e.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(e => e.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.DispatchedToRedisAtUtc)
            .HasColumnName("dispatched_to_redis_at_utc");

        builder.Property(e => e.DispatchAttempts)
            .HasColumnName("dispatch_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.NextRetryAtUtc)
            .HasColumnName("next_retry_at_utc");

        builder.Property(e => e.LastDispatchError)
            .HasColumnName("last_dispatch_error")
            .HasMaxLength(500);

        builder.HasIndex(e => new { e.DispatchedToRedisAtUtc, e.NextRetryAtUtc, e.Id });
        builder.HasIndex(e => new { e.DispatchedToRedisAtUtc, e.Id });
        builder.HasIndex(e => new { e.ScopeType, e.ScopeKey, e.NewEpoch });
    }
}
