using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PickTaskStatusHistoryConfiguration : IEntityTypeConfiguration<PickTaskStatusHistory>
{
    public void Configure(EntityTypeBuilder<PickTaskStatusHistory> builder)
    {
        builder.ToTable("pick_task_status_histories");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasColumnName("history_id")
            .ValueGeneratedNever();

        builder.Property(h => h.PickTaskId)
            .HasColumnName("pick_task_id")
            .IsRequired();

        builder.Property(h => h.FromStatus)
            .HasColumnName("from_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(h => h.ToStatus)
            .HasColumnName("to_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(h => h.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500);

        builder.Property(h => h.ChangedByUserId)
            .HasColumnName("changed_by_user_id")
            .IsRequired();

        builder.Property(h => h.ChangedAtUtc)
            .HasColumnName("changed_at_utc")
            .IsRequired();

        // Indexes
        builder.HasIndex(h => h.PickTaskId);
        builder.HasIndex(h => h.ChangedAtUtc);

        // Relationships
        builder.HasOne(h => h.PickTask)
            .WithMany(t => t.StatusHistories)
            .HasForeignKey(h => h.PickTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

