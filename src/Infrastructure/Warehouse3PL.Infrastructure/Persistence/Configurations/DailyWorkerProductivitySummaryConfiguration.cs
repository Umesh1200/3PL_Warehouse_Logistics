using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DailyWorkerProductivitySummaryConfiguration : IEntityTypeConfiguration<DailyWorkerProductivitySummary>
{
    public void Configure(EntityTypeBuilder<DailyWorkerProductivitySummary> builder)
    {
        builder.ToTable("daily_worker_productivity_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("summary_id")
            .ValueGeneratedNever();

        builder.Property(s => s.WorkerUserId).HasColumnName("worker_user_id").IsRequired();
        builder.Property(s => s.FacilityId).HasColumnName("facility_id").IsRequired();

        builder.Property(s => s.Department)
            .HasColumnName("department")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.DateUtc).HasColumnName("date_utc").IsRequired();

        builder.Property(s => s.TimeGrain)
            .HasColumnName("time_grain")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Shift & Duration
        builder.Property(s => s.TotalShiftMinutes).HasColumnName("total_shift_minutes").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.ProductiveWorkMinutes).HasColumnName("productive_work_minutes").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.IdleMinutes).HasColumnName("idle_minutes").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.ScheduledBreakMinutes).HasColumnName("scheduled_break_minutes").HasPrecision(18, 2).IsRequired();

        // Throughput Counts
        builder.Property(s => s.TasksCompleted).HasColumnName("tasks_completed").IsRequired();
        builder.Property(s => s.UnitsCompleted).HasColumnName("units_completed").IsRequired();
        builder.Property(s => s.LinesCompleted).HasColumnName("lines_completed").IsRequired();
        builder.Property(s => s.CartonsCompleted).HasColumnName("cartons_completed").IsRequired();
        builder.Property(s => s.PalletsCompleted).HasColumnName("pallets_completed").IsRequired();

        // Direct Labor Hours
        builder.Property(s => s.ActualLaborHours).HasColumnName("actual_labor_hours").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.StandardLaborHours).HasColumnName("standard_labor_hours").HasPrecision(18, 4).IsRequired();

        // Pacing & Rates
        builder.Property(s => s.LaborEfficiencyPercent).HasColumnName("labor_efficiency_percent").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.LaborUtilizationPercent).HasColumnName("labor_utilization_percent").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.UnitsPerHour).HasColumnName("units_per_hour").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.LinesPerHour).HasColumnName("lines_per_hour").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.CartonsPerHour).HasColumnName("cartons_per_hour").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TasksPerHour).HasColumnName("tasks_per_hour").HasPrecision(18, 2).IsRequired();

        builder.Property(s => s.PerformanceTier)
            .HasColumnName("performance_tier")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Financials
        builder.Property(s => s.HourlyWageRate).HasColumnName("hourly_wage_rate").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalLaborCost).HasColumnName("total_labor_cost").HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.LaborCostPerUnit).HasColumnName("labor_cost_per_unit").HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.LaborCostPerTask).HasColumnName("labor_cost_per_task").HasPrecision(18, 4).IsRequired();

        builder.Property(s => s.LastTaskCompletedAtUtc).HasColumnName("last_task_completed_at_utc").IsRequired(false);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").IsRequired(false);
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by").IsRequired(false);

        builder.HasIndex(s => new { s.WorkerUserId, s.DateUtc })
            .IsUnique()
            .HasDatabaseName("ix_daily_worker_productivity_worker_date");
        builder.HasIndex(s => new { s.FacilityId, s.DateUtc })
            .HasDatabaseName("ix_daily_worker_productivity_facility_date");
        builder.HasIndex(s => new { s.Department, s.DateUtc })
            .HasDatabaseName("ix_daily_worker_productivity_dept_date");
    }
}
