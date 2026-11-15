using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PackingSessionConfiguration : IEntityTypeConfiguration<PackingSession>
{
    public void Configure(EntityTypeBuilder<PackingSession> builder)
    {
        builder.ToTable("packing_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("session_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.PackingStationLocationId)
            .HasColumnName("packing_station_location_id")
            .IsRequired();

        builder.Property(s => s.OutboundOrderId)
            .HasColumnName("outbound_order_id")
            .IsRequired();

        builder.Property(s => s.OperatorUserId)
            .HasColumnName("operator_user_id")
            .IsRequired();

        builder.Property(s => s.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .IsRequired();

        builder.Property(s => s.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        // Soft delete
        builder.Property(s => s.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(s => s.DeletedAt).HasColumnName("deleted_at");
        builder.Property(s => s.DeletedBy).HasColumnName("deleted_by");

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(s => new { s.FacilityId, s.OutboundOrderId });
        builder.HasIndex(s => s.ClientId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.OperatorUserId);

        // Relationships
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.PackingStationLocation)
            .WithMany()
            .HasForeignKey(s => s.PackingStationLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.OutboundOrder)
            .WithMany()
            .HasForeignKey(s => s.OutboundOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.OperatorUser)
            .WithMany()
            .HasForeignKey(s => s.OperatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

