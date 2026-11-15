using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Dock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DockAssignmentConfiguration : IEntityTypeConfiguration<DockAssignment>
{
    public void Configure(EntityTypeBuilder<DockAssignment> builder)
    {
        builder.ToTable("dock_assignments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("assignment_id")
            .ValueGeneratedNever();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.DockDoorId)
            .HasColumnName("dock_door_id")
            .IsRequired();

        builder.Property(a => a.YardVisitId)
            .HasColumnName("yard_visit_id")
            .IsRequired();

        builder.Property(a => a.AsnId)
            .HasColumnName("asn_id");

        builder.Property(a => a.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.Property(a => a.ArrivedAtDock)
            .HasColumnName("arrived_at_dock");

        builder.Property(a => a.ReleasedAt)
            .HasColumnName("released_at");

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.AssignedByUserId)
            .HasColumnName("assigned_by_user_id");

        builder.Property(a => a.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(a => a.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        // Optimistic concurrency token
        builder.Property(a => a.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(a => a.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(a => a.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(a => a.DeletedBy)
            .HasColumnName("deleted_by");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(a => a.UpdatedBy)
            .HasColumnName("updated_by");

        // Relationships
        builder.HasOne(a => a.Facility)
            .WithMany()
            .HasForeignKey(a => a.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.DockDoor)
            .WithMany(d => d.Assignments)
            .HasForeignKey(a => a.DockDoorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.YardVisit)
            .WithMany(y => y.Assignments)
            .HasForeignKey(a => a.YardVisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(a => a.AsnId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

