using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class LocationLockConfiguration : IEntityTypeConfiguration<LocationLock>
{
    public void Configure(EntityTypeBuilder<LocationLock> builder)
    {
        builder.ToTable("location_locks");

        builder.HasKey(ll => ll.Id);
        builder.Property(ll => ll.Id)
            .HasColumnName("lock_id")
            .ValueGeneratedNever();

        builder.Property(ll => ll.LocationId)
            .HasColumnName("location_id")
            .IsRequired();

        builder.HasIndex(ll => ll.LocationId);

        builder.Property(ll => ll.LockType)
            .HasColumnName("lock_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(ll => ll.ReasonCode)
            .HasColumnName("reason_code")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(ll => ll.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(ll => ll.LockedByUserId)
            .HasColumnName("locked_by_user");

        builder.Property(ll => ll.LockedAt)
            .HasColumnName("locked_at")
            .IsRequired();

        builder.Property(ll => ll.ReleasedAt)
            .HasColumnName("released_at");

        builder.Property(ll => ll.ReleasedByUserId)
            .HasColumnName("released_by_user");

        // Relationships
        builder.HasOne(ll => ll.Location)
            .WithMany(l => l.Locks)
            .HasForeignKey(ll => ll.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ll => ll.LockedByUser)
            .WithMany()
            .HasForeignKey(ll => ll.LockedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ll => ll.ReleasedByUser)
            .WithMany()
            .HasForeignKey(ll => ll.ReleasedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

