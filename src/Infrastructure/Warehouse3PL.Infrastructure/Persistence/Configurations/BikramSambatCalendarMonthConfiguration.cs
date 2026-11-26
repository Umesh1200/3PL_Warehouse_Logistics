using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class BikramSambatCalendarMonthConfiguration : IEntityTypeConfiguration<BikramSambatCalendarMonth>
{
    public void Configure(EntityTypeBuilder<BikramSambatCalendarMonth> builder)
    {
        builder.ToTable("bikram_sambat_calendar_months", t =>
        {
            t.HasCheckConstraint("ck_bs_months_year_range", "bs_year >= 2075 AND bs_year <= 2090");
            t.HasCheckConstraint("ck_bs_months_month_range", "bs_month >= 1 AND bs_month <= 12");
            t.HasCheckConstraint("ck_bs_months_days_range", "days_in_month >= 29 AND days_in_month <= 32");
            t.HasCheckConstraint("ck_bs_months_span", "gregorian_end_date >= gregorian_start_date");
        });

        builder.HasKey(m => new { m.BsYear, m.BsMonth });

        builder.Property(m => m.BsYear).HasColumnName("bs_year").IsRequired();
        builder.Property(m => m.BsMonth).HasColumnName("bs_month").IsRequired();
        builder.Property(m => m.MonthNameEn).HasColumnName("month_name_en").HasMaxLength(30).IsRequired();
        builder.Property(m => m.MonthNameNe).HasColumnName("month_name_ne").HasMaxLength(40).IsRequired();
        builder.Property(m => m.DaysInMonth).HasColumnName("days_in_month").IsRequired();
        builder.Property(m => m.GregorianStartDate).HasColumnName("gregorian_start_date").IsRequired();
        builder.Property(m => m.GregorianEndDate).HasColumnName("gregorian_end_date").IsRequired();
        builder.Property(m => m.FiscalYearLabel).HasColumnName("fiscal_year_label").HasMaxLength(16).IsRequired();
        builder.Property(m => m.SourceCitation).HasColumnName("source_citation").HasMaxLength(300).IsRequired();
        builder.Property(m => m.SeedVersion).HasColumnName("seed_version").HasDefaultValue(1).IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(m => m.GregorianStartDate).IsUnique();
        builder.HasIndex(m => m.GregorianEndDate).IsUnique();
        builder.HasIndex(m => new { m.GregorianStartDate, m.GregorianEndDate });
    }
}
