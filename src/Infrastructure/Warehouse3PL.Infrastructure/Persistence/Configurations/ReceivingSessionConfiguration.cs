using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ReceivingSessionConfiguration : IEntityTypeConfiguration<ReceivingSession>
{
    public void Configure(EntityTypeBuilder<ReceivingSession> builder)
    {
        builder.ToTable("receiving_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("session_id")
            .ValueGeneratedNever();

        builder.Property(s => s.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(s => s.DockDoorId)
            .HasColumnName("dock_door_id");

        builder.Property(s => s.YardVisitId)
            .HasColumnName("yard_visit_id");

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id");

        builder.Property(s => s.AsnId)
            .HasColumnName("asn_id");

        builder.Property(s => s.PurchaseOrderId)
            .HasColumnName("purchase_order_id");

        builder.Property(s => s.SessionNumber)
            .HasColumnName("session_number")
            .HasMaxLength(60)
            .IsRequired();

        builder.HasIndex(s => new { s.FacilityId, s.SessionNumber })
            .IsUnique();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .IsRequired();

        builder.Property(s => s.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(s => s.ReceivedByUserId)
            .HasColumnName("received_by_user_id")
            .IsRequired();

        builder.Property(s => s.InitialProbeTemperature)
            .HasColumnName("initial_probe_temperature")
            .HasPrecision(5, 2);

        builder.Property(s => s.SealNumber)
            .HasColumnName("seal_number")
            .HasMaxLength(50);

        builder.Property(s => s.SealIntact)
            .HasColumnName("seal_intact");

        builder.Property(s => s.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(s => s.IsBlindReceiving)
            .HasColumnName("is_blind_receiving")
            .HasDefaultValue(false);

        builder.Property(s => s.RequiresSupervisorApproval)
            .HasColumnName("requires_supervisor_approval")
            .HasDefaultValue(false);

        builder.Property(s => s.IsSupervisorApproved)
            .HasColumnName("is_supervisor_approved")
            .HasDefaultValue(false);

        builder.Property(s => s.ApprovedByUserId)
            .HasColumnName("approved_by_user_id");

        builder.Property(s => s.ApprovedAtUtc)
            .HasColumnName("approved_at_utc");

        // Optimistic concurrency token
        builder.Property(s => s.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(s => s.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(s => s.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(s => s.DeletedBy)
            .HasColumnName("deleted_by");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(s => s.UpdatedBy)
            .HasColumnName("updated_by");

        // Relationships
        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.DockDoor)
            .WithMany()
            .HasForeignKey(s => s.DockDoorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.YardVisit)
            .WithMany()
            .HasForeignKey(s => s.YardVisitId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(s => s.AsnId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.PurchaseOrder)
            .WithMany()
            .HasForeignKey(s => s.PurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.ReceivedByUser)
            .WithMany()
            .HasForeignKey(s => s.ReceivedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ApprovedByUser)
            .WithMany()
            .HasForeignKey(s => s.ApprovedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(s => s.Lines)
            .WithOne(l => l.ReceivingSession)
            .HasForeignKey(l => l.ReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Discrepancies)
            .WithOne(d => d.ReceivingSession)
            .HasForeignKey(d => d.ReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
