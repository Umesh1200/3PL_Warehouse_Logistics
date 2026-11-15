using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Dock;

/// <summary>
/// Scheduled arrival and dock reservation time-window for an inbound ASN.
/// DB-SPEC-3PL-2026 Module 9 (dock_appointments).
/// </summary>
public class DockAppointment : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid FacilityId { get; set; }
    public Guid ClientId { get; set; }
    public Guid AsnId { get; set; }
    public Guid? DockDoorId { get; set; }

    public string AppointmentNumber { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public DockAppointmentStatus Status { get; set; } = DockAppointmentStatus.Scheduled;

    public string? CarrierName { get; set; }
    public string? TrailerNumber { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? Notes { get; set; }

    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking on scheduling updates.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public Client Client { get; set; } = null!;
    public AdvanceShippingNotice AdvanceShippingNotice { get; set; } = null!;
    public DockDoor? DockDoor { get; set; }

    public DockAppointment()
    {
        Id = Guid.NewGuid();
    }

    public void Reschedule(DateTime newStartTime, DateTime newEndTime, Guid? newDockDoorId = null)
    {
        if (Status != DockAppointmentStatus.Scheduled)
        {
            throw new DomainException($"Cannot reschedule appointment '{AppointmentNumber}' because it is in '{Status}' status. Only Scheduled appointments can be rescheduled.");
        }

        if (newEndTime <= newStartTime)
        {
            throw new DomainException("Appointment end time must be after start time.");
        }

        StartTime = newStartTime;
        EndTime = newEndTime;
        if (newDockDoorId.HasValue)
        {
            DockDoorId = newDockDoorId.Value;
        }
        Version++;
    }

    public void Cancel(string reason)
    {
        if (Status == DockAppointmentStatus.Cancelled)
        {
            return;
        }

        if (Status != DockAppointmentStatus.Scheduled)
        {
            throw new DomainException($"Cannot cancel appointment '{AppointmentNumber}' in '{Status}' status.");
        }

        Status = DockAppointmentStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = DateTime.UtcNow;
        Version++;
    }

    public void MarkCheckedIn()
    {
        if (Status != DockAppointmentStatus.Scheduled)
        {
            throw new DomainException($"Cannot check in appointment '{AppointmentNumber}' in '{Status}' status.");
        }

        Status = DockAppointmentStatus.CheckedIn;
        Version++;
    }

    public void Complete()
    {
        Status = DockAppointmentStatus.Completed;
        Version++;
    }
}

