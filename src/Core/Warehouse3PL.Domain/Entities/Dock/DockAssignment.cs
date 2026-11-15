using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Dock;

/// <summary>
/// Operational assignment linking a physical dock door with an arriving yard visit / inbound shipment.
/// DB-SPEC-3PL-2026 Module 9.
/// </summary>
public class DockAssignment : AuditableEntity<Guid>, ISoftDeletable
{
    public Guid FacilityId { get; set; }
    public Guid DockDoorId { get; set; }
    public Guid YardVisitId { get; set; }
    public Guid? AsnId { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArrivedAtDock { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DockAssignmentStatus Status { get; set; } = DockAssignmentStatus.Assigned;

    public Guid? AssignedByUserId { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking on assignment changes.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public DockDoor DockDoor { get; set; } = null!;
    public YardVisit YardVisit { get; set; } = null!;
    public AdvanceShippingNotice? AdvanceShippingNotice { get; set; }

    public DockAssignment()
    {
        Id = Guid.NewGuid();
    }

    public void ConfirmAtDock()
    {
        if (Status != DockAssignmentStatus.Assigned)
        {
            throw new DomainException($"Cannot confirm arrival at dock from '{Status}' status.");
        }

        Status = DockAssignmentStatus.AtDock;
        ArrivedAtDock = DateTime.UtcNow;
        Version++;
    }

    public void ReleaseDock(string? notes = null)
    {
        if (Status != DockAssignmentStatus.AtDock && Status != DockAssignmentStatus.Assigned)
        {
            throw new DomainException($"Cannot release dock assignment from '{Status}' status.");
        }

        Status = DockAssignmentStatus.Completed;
        ReleasedAt = DateTime.UtcNow;
        Notes = notes ?? Notes;
        Version++;
    }

    public void Cancel(string reason)
    {
        if (Status == DockAssignmentStatus.Completed || Status == DockAssignmentStatus.Cancelled)
        {
            return;
        }

        Status = DockAssignmentStatus.Cancelled;
        CancellationReason = reason;
        Version++;
    }
}

