using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReturnReceivingSessionConfiguration : IEntityTypeConfiguration<ReturnReceivingSession>
{
    public void Configure(EntityTypeBuilder<ReturnReceivingSession> builder)
    {
        builder.ToTable("return_receiving_sessions");

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

        builder.Property(s => s.ReturnInboundHandoffId)
            .HasColumnName("return_inbound_handoff_id")
            .IsRequired();

        builder.Property(s => s.ReturnShipmentId)
            .HasColumnName("return_shipment_id")
            .IsRequired();

        builder.Property(s => s.ReturnAuthorizationId)
            .HasColumnName("return_authorization_id")
            .IsRequired();

        builder.Property(s => s.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(s => s.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(s => s.SessionNumber)
            .HasColumnName("session_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.ReceivedByUserId)
            .HasColumnName("received_by_user_id")
            .IsRequired();

        builder.Property(s => s.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .IsRequired();

        builder.Property(s => s.PhysicalArrivalAtUtc)
            .HasColumnName("physical_arrival_at_utc");

        builder.Property(s => s.InspectedAtUtc)
            .HasColumnName("inspected_at_utc");

        builder.Property(s => s.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(s => s.PackageCondition)
            .HasColumnName("package_condition")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.PackageConditionNotes)
            .HasColumnName("package_condition_notes")
            .HasMaxLength(500);

        builder.Property(s => s.IsInventoryPosted)
            .HasColumnName("is_inventory_posted")
            .IsRequired();

        builder.Property(s => s.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(s => s.ReturnInboundHandoffId)
            .HasDatabaseName("ix_return_receiving_sessions_handoff")
            .IsUnique();

        builder.HasIndex(s => new { s.ClientId, s.SessionNumber })
            .HasDatabaseName("ix_return_receiving_sessions_client_num")
            .IsUnique();

        builder.HasIndex(s => new { s.FacilityId, s.Status })
            .HasDatabaseName("ix_return_receiving_sessions_facility_status");

        // Relationships
        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ReturnInboundHandoff)
            .WithMany()
            .HasForeignKey(s => s.ReturnInboundHandoffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ReturnShipment)
            .WithMany()
            .HasForeignKey(s => s.ReturnShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ReturnAuthorization)
            .WithMany()
            .HasForeignKey(s => s.ReturnAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ReceivedByUser)
            .WithMany()
            .HasForeignKey(s => s.ReceivedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Lines)
            .WithOne(l => l.ReturnReceivingSession)
            .HasForeignKey(l => l.ReturnReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Discrepancies)
            .WithOne(d => d.ReturnReceivingSession)
            .HasForeignKey(d => d.ReturnReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

