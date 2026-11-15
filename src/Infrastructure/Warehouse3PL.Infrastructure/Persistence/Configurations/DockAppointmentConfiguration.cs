using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Dock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DockAppointmentConfiguration : IEntityTypeConfiguration<DockAppointment>
{
    public void Configure(EntityTypeBuilder<DockAppointment> builder)
    {
        builder.ToTable("dock_appointments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("appointment_id")
            .ValueGeneratedNever();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.AsnId)
            .HasColumnName("asn_id")
            .IsRequired();

        builder.Property(a => a.DockDoorId)
            .HasColumnName("dock_door_id");

        // Unique appointment number per facility
        builder.HasIndex(a => new { a.FacilityId, a.AppointmentNumber })
            .IsUnique();

        builder.Property(a => a.AppointmentNumber)
            .HasColumnName("appointment_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.StartTime)
            .HasColumnName("start_time")
            .IsRequired();

        builder.Property(a => a.EndTime)
            .HasColumnName("end_time")
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.CarrierName)
            .HasColumnName("carrier_name")
            .HasMaxLength(150);

        builder.Property(a => a.TrailerNumber)
            .HasColumnName("trailer_number")
            .HasMaxLength(50);

        builder.Property(a => a.DriverName)
            .HasColumnName("driver_name")
            .HasMaxLength(150);

        builder.Property(a => a.DriverPhone)
            .HasColumnName("driver_phone")
            .HasMaxLength(50);

        builder.Property(a => a.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(a => a.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        builder.Property(a => a.CancelledAt)
            .HasColumnName("cancelled_at");

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

        builder.HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(a => a.AsnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.DockDoor)
            .WithMany(d => d.Appointments)
            .HasForeignKey(a => a.DockDoorId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

