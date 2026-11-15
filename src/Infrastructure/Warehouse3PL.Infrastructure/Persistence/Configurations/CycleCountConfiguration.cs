using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CycleCountConfiguration : IEntityTypeConfiguration<CycleCount>
{
    public void Configure(EntityTypeBuilder<CycleCount> builder)
    {
        builder.ToTable("cycle_counts");

        builder.HasKey(cc => cc.Id);
        builder.Property(cc => cc.Id)
            .HasColumnName("count_id")
            .ValueGeneratedNever();

        builder.Property(cc => cc.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(cc => cc.ZoneId)
            .HasColumnName("zone_id");

        builder.Property(cc => cc.CountNumber)
            .HasColumnName("count_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(cc => cc.CountNumber).IsUnique();

        builder.Property(cc => cc.CountType)
            .HasColumnName("count_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(cc => cc.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(cc => cc.ScheduledDate)
            .HasColumnName("scheduled_date")
            .IsRequired();

        builder.Property(cc => cc.StartedAt).HasColumnName("started_at");
        builder.Property(cc => cc.CompletedAt).HasColumnName("completed_at");
        builder.Property(cc => cc.Notes).HasColumnName("notes").HasMaxLength(500);

        // Auditing
        builder.Property(cc => cc.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(cc => cc.CreatedBy).HasColumnName("created_by");
        builder.Property(cc => cc.UpdatedAt).HasColumnName("updated_at");
        builder.Property(cc => cc.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(cc => cc.FacilityId);
        builder.HasIndex(cc => cc.Status);

        // Relationships
        builder.HasOne(cc => cc.Facility)
            .WithMany()
            .HasForeignKey(cc => cc.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cc => cc.Zone)
            .WithMany()
            .HasForeignKey(cc => cc.ZoneId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

