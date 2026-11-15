using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class RateCardLineConfiguration : IEntityTypeConfiguration<RateCardLine>
{
    public void Configure(EntityTypeBuilder<RateCardLine> builder)
    {
        builder.ToTable("rate_card_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("line_id")
            .ValueGeneratedNever();

        builder.Property(l => l.RateCardScheduleId)
            .HasColumnName("rate_card_schedule_id")
            .IsRequired();

        builder.Property(l => l.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(l => l.ActivityType)
            .HasColumnName("activity_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.UnitOfMeasure)
            .HasColumnName("unit_of_measure")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(l => l.RateModel)
            .HasColumnName("rate_model")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.TierModel)
            .HasColumnName("tier_model")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.BaseRate)
            .HasColumnName("base_rate")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.MinimumCharge)
            .HasColumnName("minimum_charge")
            .HasPrecision(18, 4);

        // Auditing
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.CreatedBy).HasColumnName("created_by");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(l => new { l.RateCardScheduleId, l.ActivityType })
            .HasDatabaseName("ix_rate_card_lines_schedule_activity")
            .IsUnique();

        // Relationships
        builder.HasOne(l => l.RateCardSchedule)
            .WithMany(s => s.Lines)
            .HasForeignKey(l => l.RateCardScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Tiers)
            .WithOne(t => t.RateCardLine)
            .HasForeignKey(t => t.RateCardLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

