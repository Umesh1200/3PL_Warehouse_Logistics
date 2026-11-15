using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PickWaveConfiguration : IEntityTypeConfiguration<PickWave>
{
    public void Configure(EntityTypeBuilder<PickWave> builder)
    {
        builder.ToTable("pick_waves");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasColumnName("pick_wave_id")
            .ValueGeneratedNever();

        builder.Property(w => w.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(w => w.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(w => w.WaveNumber)
            .HasColumnName("wave_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(w => w.WaveType)
            .HasColumnName("wave_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(w => w.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(w => w.Priority)
            .HasColumnName("priority")
            .IsRequired();

        builder.Property(w => w.ZoneId)
            .HasColumnName("zone_id");

        builder.Property(w => w.TempClass)
            .HasColumnName("temp_class")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(w => w.AssignedToUserId)
            .HasColumnName("assigned_to_user_id");

        builder.Property(w => w.ReleasedAtUtc)
            .HasColumnName("released_at_utc");

        builder.Property(w => w.StartedAtUtc)
            .HasColumnName("started_at_utc");

        builder.Property(w => w.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(w => w.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        builder.Property(w => w.CancelledAtUtc)
            .HasColumnName("cancelled_at_utc");

        builder.Property(w => w.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1u);

        // Soft delete
        builder.Property(w => w.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(w => w.DeletedAt).HasColumnName("deleted_at");
        builder.Property(w => w.DeletedBy).HasColumnName("deleted_by");

        // Auditing
        builder.Property(w => w.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(w => w.CreatedBy).HasColumnName("created_by");
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at");
        builder.Property(w => w.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(w => new { w.FacilityId, w.WaveNumber }).IsUnique();
        builder.HasIndex(w => w.ClientId);
        builder.HasIndex(w => w.Status);
        builder.HasIndex(w => w.ZoneId);
        builder.HasIndex(w => w.AssignedToUserId);

        // Relationships
        builder.HasOne(w => w.Client)
            .WithMany()
            .HasForeignKey(w => w.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.Facility)
            .WithMany()
            .HasForeignKey(w => w.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.Zone)
            .WithMany()
            .HasForeignKey(w => w.ZoneId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(w => w.AssignedToUser)
            .WithMany()
            .HasForeignKey(w => w.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(w => w.WaveOrders)
            .WithOne(o => o.PickWave)
            .HasForeignKey(o => o.PickWaveId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(w => w.Tasks)
            .WithOne(t => t.PickWave)
            .HasForeignKey(t => t.PickWaveId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

