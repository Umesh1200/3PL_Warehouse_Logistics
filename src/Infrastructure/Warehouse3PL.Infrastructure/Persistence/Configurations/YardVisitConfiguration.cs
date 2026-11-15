using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Dock;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class YardVisitConfiguration : IEntityTypeConfiguration<YardVisit>
{
    public void Configure(EntityTypeBuilder<YardVisit> builder)
    {
        builder.ToTable("yard_visits");

        builder.HasKey(y => y.Id);
        builder.Property(y => y.Id)
            .HasColumnName("yard_visit_id")
            .ValueGeneratedNever();

        builder.Property(y => y.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(y => y.ClientId)
            .HasColumnName("client_id");

        builder.Property(y => y.AsnId)
            .HasColumnName("asn_id");

        builder.Property(y => y.DockAppointmentId)
            .HasColumnName("dock_appointment_id");

        // Unique visit number per facility
        builder.HasIndex(y => new { y.FacilityId, y.VisitNumber })
            .IsUnique();

        builder.Property(y => y.VisitNumber)
            .HasColumnName("visit_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(y => y.CarrierName)
            .HasColumnName("carrier_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(y => y.TrailerNumber)
            .HasColumnName("trailer_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(y => y.LicensePlate)
            .HasColumnName("license_plate")
            .HasMaxLength(50);

        builder.Property(y => y.DriverName)
            .HasColumnName("driver_name")
            .HasMaxLength(150);

        builder.Property(y => y.DriverPhone)
            .HasColumnName("driver_phone")
            .HasMaxLength(50);

        builder.Property(y => y.CheckInTime)
            .HasColumnName("check_in_time")
            .IsRequired();

        builder.Property(y => y.DepartureTime)
            .HasColumnName("departure_time");

        builder.Property(y => y.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(y => y.ParkingLocation)
            .HasColumnName("parking_location")
            .HasMaxLength(50);

        builder.Property(y => y.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Optimistic concurrency token
        builder.Property(y => y.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(y => y.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(y => y.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(y => y.DeletedBy)
            .HasColumnName("deleted_by");

        builder.Property(y => y.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(y => y.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(y => y.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(y => y.UpdatedBy)
            .HasColumnName("updated_by");

        // Relationships
        builder.HasOne(y => y.Facility)
            .WithMany()
            .HasForeignKey(y => y.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(y => y.Client)
            .WithMany()
            .HasForeignKey(y => y.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(y => y.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(y => y.AsnId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(y => y.DockAppointment)
            .WithMany()
            .HasForeignKey(y => y.DockAppointmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(y => y.Assignments)
            .WithOne(a => a.YardVisit)
            .HasForeignKey(a => a.YardVisitId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

