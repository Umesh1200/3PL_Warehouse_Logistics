using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.CrossDock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CrossDockExceptionConfiguration : IEntityTypeConfiguration<CrossDockException>
{
    public void Configure(EntityTypeBuilder<CrossDockException> builder)
    {
        builder.ToTable("cross_dock_exceptions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("exception_id").ValueGeneratedNever();

        builder.Property(e => e.CrossDockPlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(e => e.CrossDockTaskId).HasColumnName("task_id");
        builder.Property(e => e.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.QuantityDiscrepant).HasColumnName("quantity_discrepant").HasPrecision(14, 4).IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        builder.Property(e => e.ReportedByUserId).HasColumnName("reported_by_user_id");
        builder.Property(e => e.ReportedAtUtc).HasColumnName("reported_at_utc").IsRequired();
        builder.Property(e => e.IsResolved).HasColumnName("is_resolved").IsRequired();
        builder.Property(e => e.ResolutionNotes).HasColumnName("resolution_notes").HasMaxLength(500);
        builder.Property(e => e.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        builder.Property(e => e.Version).HasColumnName("version").IsConcurrencyToken();

        // Indexes
        builder.HasIndex(e => new { e.CrossDockPlanId, e.IsResolved });

        // Relationships
        builder.HasOne(e => e.CrossDockTask).WithMany().HasForeignKey(e => e.CrossDockTaskId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.ReportedByUser).WithMany().HasForeignKey(e => e.ReportedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

