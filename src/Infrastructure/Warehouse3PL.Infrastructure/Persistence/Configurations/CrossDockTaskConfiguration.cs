using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.CrossDock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CrossDockTaskConfiguration : IEntityTypeConfiguration<CrossDockTask>
{
    public void Configure(EntityTypeBuilder<CrossDockTask> builder)
    {
        builder.ToTable("cross_dock_tasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("task_id").ValueGeneratedNever();

        builder.Property(t => t.CrossDockPlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(t => t.CrossDockPlanLineId).HasColumnName("plan_line_id").IsRequired();
        builder.Property(t => t.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(t => t.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(t => t.ItemId).HasColumnName("item_id").IsRequired();
        builder.Property(t => t.LotId).HasColumnName("lot_id");
        builder.Property(t => t.TaskNumber).HasColumnName("task_number").HasMaxLength(100).IsRequired();
        builder.Property(t => t.Lpn).HasColumnName("lpn").HasMaxLength(100).IsRequired();
        builder.Property(t => t.SourceLocationId).HasColumnName("source_location_id").IsRequired();
        builder.Property(t => t.DestinationLocationId).HasColumnName("destination_location_id").IsRequired();
        builder.Property(t => t.ActualDestinationLocationId).HasColumnName("actual_destination_location_id");
        builder.Property(t => t.Quantity).HasColumnName("quantity").HasPrecision(14, 4).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(t => t.Priority).HasColumnName("priority").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(t => t.AssignedUserId).HasColumnName("assigned_user_id");
        builder.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(t => t.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(t => t.ScannedLpn).HasColumnName("scanned_lpn").HasMaxLength(100);
        builder.Property(t => t.ScannedDestinationBarcode).HasColumnName("scanned_destination_barcode").HasMaxLength(100);
        builder.Property(t => t.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(t => t.Version).HasColumnName("version").IsConcurrencyToken();

        // Auditing
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.CreatedBy).HasColumnName("created_by");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(t => new { t.FacilityId, t.TaskNumber }).IsUnique();
        builder.HasIndex(t => new { t.FacilityId, t.Status, t.Priority });
        builder.HasIndex(t => t.Lpn);

        // Relationships
        builder.HasOne(t => t.Client).WithMany().HasForeignKey(t => t.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Facility).WithMany().HasForeignKey(t => t.FacilityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Item).WithMany().HasForeignKey(t => t.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Lot).WithMany().HasForeignKey(t => t.LotId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(t => t.SourceLocation).WithMany().HasForeignKey(t => t.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.DestinationLocation).WithMany().HasForeignKey(t => t.DestinationLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.ActualDestinationLocation).WithMany().HasForeignKey(t => t.ActualDestinationLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.AssignedUser).WithMany().HasForeignKey(t => t.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

