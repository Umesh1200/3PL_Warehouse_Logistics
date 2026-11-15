using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class WorkerIdleIntervalRecordConfiguration : IEntityTypeConfiguration<WorkerIdleIntervalRecord>
{
    public void Configure(EntityTypeBuilder<WorkerIdleIntervalRecord> builder)
    {
        builder.ToTable("worker_idle_interval_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("idle_record_id")
            .ValueGeneratedNever();

        builder.Property(r => r.WorkerUserId).HasColumnName("worker_user_id").IsRequired();
        builder.Property(r => r.FacilityId).HasColumnName("facility_id").IsRequired();

        builder.Property(r => r.Department)
            .HasColumnName("department")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(r => r.EndedAtUtc).HasColumnName("ended_at_utc").IsRequired();

        builder.Property(r => r.DurationMinutes).HasColumnName("duration_minutes").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.DurationSeconds).HasColumnName("duration_seconds").HasPrecision(18, 2).IsRequired();

        builder.Property(r => r.ReasonCode)
            .HasColumnName("reason_code")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.SupervisorApproved).HasColumnName("supervisor_approved").IsRequired();
        builder.Property(r => r.Notes).HasColumnName("notes").HasMaxLength(500).IsRequired(false);

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(r => new { r.WorkerUserId, r.StartedAtUtc })
            .HasDatabaseName("ix_worker_idle_interval_records_worker_started");
        builder.HasIndex(r => new { r.FacilityId, r.StartedAtUtc })
            .HasDatabaseName("ix_worker_idle_interval_records_facility_started");
    }
}
