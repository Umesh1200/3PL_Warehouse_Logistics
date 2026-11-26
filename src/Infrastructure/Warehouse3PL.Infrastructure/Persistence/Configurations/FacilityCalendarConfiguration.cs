using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class FacilityCalendarConfiguration : IEntityTypeConfiguration<FacilityCalendar>
{
    public void Configure(EntityTypeBuilder<FacilityCalendar> builder)
    {
        builder.ToTable("facility_calendars", t =>
        {
            t.HasCheckConstraint(
                "ck_facility_calendars_window",
                "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
            t.HasCheckConstraint(
                "ck_facility_calendars_hours",
                "workday_end_local_time > workday_start_local_time AND default_cutoff_local_time >= workday_start_local_time AND default_cutoff_local_time <= workday_end_local_time");
        });

        builder.HasKey(fc => fc.Id);
        builder.Property(fc => fc.Id).HasColumnName("facility_calendar_id").ValueGeneratedNever();

        builder.Property(fc => fc.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(fc => fc.EffectiveFromUtc).HasColumnName("effective_from_utc").IsRequired();
        builder.Property(fc => fc.EffectiveToUtc).HasColumnName("effective_to_utc");
        builder.Property(fc => fc.WeeklyRestDaysCsv).HasColumnName("weekly_rest_days_csv").HasMaxLength(80).IsRequired();
        builder.Property(fc => fc.WorkdayStartLocalTime).HasColumnName("workday_start_local_time").IsRequired();
        builder.Property(fc => fc.WorkdayEndLocalTime).HasColumnName("workday_end_local_time").IsRequired();
        builder.Property(fc => fc.DefaultCutoffLocalTime).HasColumnName("default_cutoff_local_time").IsRequired();

        builder.Property(fc => fc.ConfigRevision)
            .HasColumnName("config_revision")
            .IsConcurrencyToken()
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(fc => fc.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.Property(fc => fc.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(fc => fc.CreatedBy).HasColumnName("created_by");
        builder.Property(fc => fc.UpdatedAt).HasColumnName("updated_at");
        builder.Property(fc => fc.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(fc => new { fc.FacilityId, fc.IsActive, fc.EffectiveFromUtc });

        builder.HasOne(fc => fc.Facility)
            .WithMany()
            .HasForeignKey(fc => fc.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
