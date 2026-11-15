using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class WorkerTaskExecutionLogConfiguration : IEntityTypeConfiguration<WorkerTaskExecutionLog>
{
    public void Configure(EntityTypeBuilder<WorkerTaskExecutionLog> builder)
    {
        builder.ToTable("worker_task_execution_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("execution_log_id")
            .ValueGeneratedNever();

        builder.Property(l => l.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(l => l.WorkerUserId).HasColumnName("worker_user_id").IsRequired();
        builder.Property(l => l.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(l => l.ZoneId).HasColumnName("zone_id").IsRequired(false);

        builder.Property(l => l.ActivityType)
            .HasColumnName("activity_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.Department)
            .HasColumnName("department")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(l => l.UnitsProcessed).HasColumnName("units_processed").IsRequired();
        builder.Property(l => l.LinesProcessed).HasColumnName("lines_processed").IsRequired();
        builder.Property(l => l.CartonsProcessed).HasColumnName("cartons_processed").IsRequired();
        builder.Property(l => l.PalletsProcessed).HasColumnName("pallets_processed").IsRequired();
        builder.Property(l => l.TravelDistanceMeters).HasColumnName("travel_distance_meters").HasPrecision(18, 2).IsRequired();

        builder.Property(l => l.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(l => l.CompletedAtUtc).HasColumnName("completed_at_utc").IsRequired();

        builder.Property(l => l.ActualDurationSeconds).HasColumnName("actual_duration_seconds").HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.StandardDurationSeconds).HasColumnName("standard_duration_seconds").HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.StandardProfileId).HasColumnName("standard_profile_id").IsRequired(false);
        builder.Property(l => l.StandardProfileVersion).HasColumnName("standard_profile_version").IsRequired();
        builder.Property(l => l.VarianceSeconds).HasColumnName("variance_seconds").HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.EfficiencyPercent).HasColumnName("efficiency_percent").HasPrecision(18, 2).IsRequired();

        builder.Property(l => l.Complexity)
            .HasColumnName("complexity")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.Equipment)
            .HasColumnName("equipment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.HourlyWageRate).HasColumnName("hourly_wage_rate").HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.CalculatedLaborCost).HasColumnName("calculated_labor_cost").HasPrecision(18, 4).IsRequired();

        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(l => l.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(l => new { l.WorkerUserId, l.CompletedAtUtc })
            .HasDatabaseName("ix_worker_task_execution_logs_worker_completed");
        builder.HasIndex(l => new { l.FacilityId, l.CompletedAtUtc })
            .HasDatabaseName("ix_worker_task_execution_logs_facility_completed");
        builder.HasIndex(l => l.TaskId)
            .HasDatabaseName("ix_worker_task_execution_logs_task_id");
    }
}
