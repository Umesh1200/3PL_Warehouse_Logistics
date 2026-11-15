using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Dock;

/// <summary>
/// A commercial vehicle or trailer's physical visit to the warehouse yard.
/// Tracks gate check-in, yard staging, dock arrival, and departure.
/// DB-SPEC-3PL-2026 Module 9.
/// </summary>
public class YardVisit : AuditableEntity<Guid>, ISoftDeletable
{
    public Guid FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? AsnId { get; set; }
    public Guid? DockAppointmentId { get; set; }

    public string VisitNumber { get; set; } = string.Empty;
    public string CarrierName { get; set; } = string.Empty;
    public string TrailerNumber { get; set; } = string.Empty;
    public string? LicensePlate { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }

    public DateTime CheckInTime { get; set; } = DateTime.UtcNow;
    public DateTime? DepartureTime { get; set; }
    public YardVisitStatus Status { get; set; } = YardVisitStatus.CheckedIn;

    /// <summary>
    /// Staging spot or parking lane while waiting in yard.
    /// </summary>
    public string? ParkingLocation { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking on yard status updates.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public Client? Client { get; set; }
    public AdvanceShippingNotice? AdvanceShippingNotice { get; set; }
    public DockAppointment? DockAppointment { get; set; }
    public ICollection<DockAssignment> Assignments { get; set; } = [];

    public YardVisit()
    {
        Id = Guid.NewGuid();
    }

    public void MoveToWaitingInYard(string? parkingLocation = null)
    {
        if (Status != YardVisitStatus.CheckedIn)
        {
            throw new DomainException($"Cannot move visit '{VisitNumber}' to WaitingInYard because status is '{Status}'.");
        }

        Status = YardVisitStatus.WaitingInYard;
        ParkingLocation = parkingLocation ?? ParkingLocation;
        Version++;
    }

    public void AssignToDock()
    {
        if (Status != YardVisitStatus.CheckedIn && Status != YardVisitStatus.WaitingInYard)
        {
            throw new DomainException($"Cannot assign visit '{VisitNumber}' to a dock door from '{Status}' status.");
        }

        Status = YardVisitStatus.AssignedToDock;
        Version++;
    }

    public void ConfirmAtDock()
    {
        if (Status != YardVisitStatus.AssignedToDock)
        {
            throw new DomainException($"Cannot confirm visit '{VisitNumber}' at dock from '{Status}' status. Must be AssignedToDock first.");
        }

        Status = YardVisitStatus.AtDock;
        Version++;
    }

    public void ReleaseFromDock()
    {
        if (Status != YardVisitStatus.AtDock)
        {
            throw new DomainException($"Cannot release visit '{VisitNumber}' from dock because it is in '{Status}' status.");
        }

        Status = YardVisitStatus.DockReleased;
        Version++;
    }

    public void RecordDeparture()
    {
        if (Status == YardVisitStatus.Departed)
        {
            return;
        }

        if (Status == YardVisitStatus.AtDock)
        {
            throw new DomainException($"Cannot depart visit '{VisitNumber}' while still docked at a door. Dock door must be released first.");
        }

        Status = YardVisitStatus.Departed;
        DepartureTime = DateTime.UtcNow;
        Version++;
    }
}

