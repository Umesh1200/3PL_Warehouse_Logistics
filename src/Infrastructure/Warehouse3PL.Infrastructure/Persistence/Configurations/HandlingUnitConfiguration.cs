using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class HandlingUnitConfiguration : IEntityTypeConfiguration<HandlingUnit>
{
    public void Configure(EntityTypeBuilder<HandlingUnit> builder)
    {
        builder.ToTable("handling_units");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasColumnName("handling_unit_id")
            .ValueGeneratedNever();

        builder.Property(h => h.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(h => h.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(h => h.LpnCode)
            .HasColumnName("lpn_code")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(h => h.UnitType)
            .HasColumnName("unit_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(h => h.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(h => h.CurrentLocationId)
            .HasColumnName("current_location_id");

        builder.Property(h => h.CurrentPickWaveId)
            .HasColumnName("current_pick_wave_id");

        builder.Property(h => h.CurrentOutboundOrderId)
            .HasColumnName("current_order_id");

        builder.Property(h => h.MaxWeightKg)
            .HasColumnName("max_weight_kg")
            .HasPrecision(14, 4);

        builder.Property(h => h.MaxVolumeCm3)
            .HasColumnName("max_volume_cm3")
            .HasPrecision(14, 4);

        builder.Property(h => h.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .HasDefaultValue(1u);

        // Soft delete
        builder.Property(h => h.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(h => h.DeletedAt).HasColumnName("deleted_at");
        builder.Property(h => h.DeletedBy).HasColumnName("deleted_by");

        // Auditing
        builder.Property(h => h.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(h => h.CreatedBy).HasColumnName("created_by");
        builder.Property(h => h.UpdatedAt).HasColumnName("updated_at");
        builder.Property(h => h.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(h => new { h.FacilityId, h.LpnCode }).IsUnique();
        builder.HasIndex(h => h.ClientId);
        builder.HasIndex(h => h.CurrentLocationId);
        builder.HasIndex(h => h.CurrentPickWaveId);
        builder.HasIndex(h => h.Status);

        // Relationships
        builder.HasOne(h => h.Client)
            .WithMany()
            .HasForeignKey(h => h.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Facility)
            .WithMany()
            .HasForeignKey(h => h.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.CurrentLocation)
            .WithMany()
            .HasForeignKey(h => h.CurrentLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(h => h.CurrentPickWave)
            .WithMany()
            .HasForeignKey(h => h.CurrentPickWaveId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(h => h.CurrentOutboundOrder)
            .WithMany()
            .HasForeignKey(h => h.CurrentOutboundOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

