using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class RateCardScheduleConfiguration : IEntityTypeConfiguration<RateCardSchedule>
{
    public void Configure(EntityTypeBuilder<RateCardSchedule> builder)
    {
        builder.ToTable("rate_card_schedules");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("schedule_id")
            .ValueGeneratedNever();

        builder.Property(s => s.RateCardId)
            .HasColumnName("rate_card_id")
            .IsRequired();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(s => s.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(s => s.EffectiveFromUtc)
            .HasColumnName("effective_from_utc")
            .IsRequired();

        builder.Property(s => s.EffectiveToUtc)
            .HasColumnName("effective_to_utc");

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.MinimumMonthlySpend)
            .HasColumnName("minimum_monthly_spend")
            .HasPrecision(18, 4);

        builder.Property(s => s.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(s => s.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(s => new { s.RateCardId, s.Version })
            .HasDatabaseName("ix_rate_card_schedules_card_version")
            .IsUnique();

        builder.HasIndex(s => new { s.RateCardId, s.EffectiveFromUtc, s.EffectiveToUtc })
            .HasDatabaseName("ix_rate_card_schedules_card_dates");

        builder.HasIndex(s => new { s.ClientId, s.FacilityId, s.Status })
            .HasDatabaseName("ix_rate_card_schedules_client_facility_status");

        // Relationships
        builder.HasOne(s => s.RateCard)
            .WithMany(rc => rc.Schedules)
            .HasForeignKey(s => s.RateCardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Lines)
            .WithOne(l => l.RateCardSchedule)
            .HasForeignKey(l => l.RateCardScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

