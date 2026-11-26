using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class HolidayCalendarConfiguration : IEntityTypeConfiguration<HolidayCalendar>
{
    public void Configure(EntityTypeBuilder<HolidayCalendar> builder)
    {
        builder.ToTable("holiday_calendars", t =>
        {
            t.HasCheckConstraint(
                "ck_holiday_calendars_scope_binding",
                "(holiday_scope = 'National' AND subdivision_code IS NULL AND facility_id IS NULL) OR " +
                "(holiday_scope = 'Regional' AND subdivision_code IS NOT NULL AND facility_id IS NULL) OR " +
                "(holiday_scope = 'Facility' AND facility_id IS NOT NULL)");
        });

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("holiday_id").ValueGeneratedNever();

        builder.Property(h => h.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        builder.Property(h => h.SubdivisionCode).HasColumnName("subdivision_code").HasMaxLength(20);
        builder.Property(h => h.FacilityId).HasColumnName("facility_id");
        builder.Property(h => h.HolidayDate).HasColumnName("holiday_date").IsRequired();
        builder.Property(h => h.Name).HasColumnName("name").HasMaxLength(120).IsRequired();

        builder.Property(h => h.HolidayScope)
            .HasColumnName("holiday_scope")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(h => h.IsWorkingDayOverride)
            .HasColumnName("is_working_day_override")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(h => h.ConfigRevision)
            .HasColumnName("config_revision")
            .IsConcurrencyToken()
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(h => h.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.Property(h => h.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(h => h.CreatedBy).HasColumnName("created_by");
        builder.Property(h => h.UpdatedAt).HasColumnName("updated_at");
        builder.Property(h => h.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(h => new { h.CountryCode, h.HolidayDate, h.IsActive });
        builder.HasIndex(h => new { h.FacilityId, h.HolidayDate, h.IsActive });

        builder.HasOne(h => h.Country)
            .WithMany()
            .HasForeignKey(h => h.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Facility)
            .WithMany()
            .HasForeignKey(h => h.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
