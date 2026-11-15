using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.CrossDock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CrossDockPlanConfiguration : IEntityTypeConfiguration<CrossDockPlan>
{
    public void Configure(EntityTypeBuilder<CrossDockPlan> builder)
    {
        builder.ToTable("cross_dock_plans");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("plan_id").ValueGeneratedNever();

        builder.Property(p => p.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(p => p.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(p => p.ReceivingSessionId).HasColumnName("receiving_session_id");
        builder.Property(p => p.PlanNumber).HasColumnName("plan_number").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.TotalPlannedQuantity).HasColumnName("total_planned_quantity").HasPrecision(14, 4).IsRequired();
        builder.Property(p => p.TotalExecutedQuantity).HasColumnName("total_executed_quantity").HasPrecision(14, 4).IsRequired();
        builder.Property(p => p.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(p => p.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken();

        // Auditing
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        // Soft delete
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        builder.Property(p => p.DeletedBy).HasColumnName("deleted_by");
        builder.HasQueryFilter(p => !p.IsDeleted);

        // Indexes
        builder.HasIndex(p => new { p.FacilityId, p.PlanNumber }).IsUnique();
        builder.HasIndex(p => new { p.ClientId, p.Status });

        // Relationships
        builder.HasOne(p => p.Client).WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Facility).WithMany().HasForeignKey(p => p.FacilityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.ReceivingSession).WithMany().HasForeignKey(p => p.ReceivingSessionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(p => p.Lines).WithOne(l => l.CrossDockPlan).HasForeignKey(l => l.CrossDockPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Tasks).WithOne(t => t.CrossDockPlan).HasForeignKey(t => t.CrossDockPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Exceptions).WithOne(e => e.CrossDockPlan).HasForeignKey(e => e.CrossDockPlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

