using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PutawayTaskConfiguration : IEntityTypeConfiguration<PutawayTask>
{
    public void Configure(EntityTypeBuilder<PutawayTask> builder)
    {
        builder.ToTable("putaway_tasks");

        builder.HasKey(pt => pt.Id);
        builder.Property(pt => pt.Id)
            .HasColumnName("task_id")
            .ValueGeneratedNever();

        builder.Property(pt => pt.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(pt => pt.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(pt => pt.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(pt => pt.LotId)
            .HasColumnName("lot_id");

        builder.Property(pt => pt.LpnId)
            .HasColumnName("lpn_id");

        builder.Property(pt => pt.LpnCode)
            .HasColumnName("lpn_code")
            .HasMaxLength(100);

        builder.Property(pt => pt.SourceLocationId)
            .HasColumnName("source_location_id")
            .IsRequired();

        builder.Property(pt => pt.SuggestedLocationId)
            .HasColumnName("suggested_location_id")
            .IsRequired();

        builder.Property(pt => pt.ActualLocationId)
            .HasColumnName("actual_location_id");

        builder.Property(pt => pt.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(pt => pt.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(pt => pt.Priority)
            .HasColumnName("priority")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(pt => pt.AssignedUserId)
            .HasColumnName("assigned_user_id");

        builder.Property(pt => pt.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(pt => pt.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(pt => pt.ExceptionReason)
            .HasColumnName("exception_reason")
            .HasMaxLength(500);

        builder.Property(pt => pt.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(pt => pt.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // Ignored aliases
        builder.Ignore(pt => pt.TargetLocationId);
        builder.Ignore(pt => pt.TargetLocation);
        builder.Ignore(pt => pt.CompletedAt);

        // Auditing
        builder.Property(pt => pt.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(pt => pt.CreatedBy).HasColumnName("created_by");
        builder.Property(pt => pt.UpdatedAt).HasColumnName("updated_at");
        builder.Property(pt => pt.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(pt => pt.ClientId);
        builder.HasIndex(pt => new { pt.FacilityId, pt.Status, pt.Priority });
        builder.HasIndex(pt => pt.LpnCode);

        // Relationships
        builder.HasOne(pt => pt.Client)
            .WithMany()
            .HasForeignKey(pt => pt.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.Facility)
            .WithMany()
            .HasForeignKey(pt => pt.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.Item)
            .WithMany()
            .HasForeignKey(pt => pt.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.Lot)
            .WithMany()
            .HasForeignKey(pt => pt.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(pt => pt.SourceLocation)
            .WithMany()
            .HasForeignKey(pt => pt.SourceLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.SuggestedLocation)
            .WithMany()
            .HasForeignKey(pt => pt.SuggestedLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.ActualLocation)
            .WithMany()
            .HasForeignKey(pt => pt.ActualLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.AssignedUser)
            .WithMany()
            .HasForeignKey(pt => pt.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(pt => pt.ReceiptLine)
            .WithMany()
            .HasForeignKey(pt => pt.LpnId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
