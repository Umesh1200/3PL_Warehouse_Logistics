using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Dock;

/// <summary>
/// Physical dock door belonging to a warehouse facility.
/// DB-SPEC-3PL-2026 Module 9 (dock_doors).
/// </summary>
public class DockDoor : AuditableEntity<Guid>, ISoftDeletable
{
    public Guid FacilityId { get; set; }
    public string DoorNumber { get; set; } = string.Empty;
    public DockDoorType DoorType { get; set; } = DockDoorType.Combination;
    public DockDoorStatus Status { get; set; } = DockDoorStatus.Active;
    
    // Physical capabilities & operational constraints
    public bool SupportsRefrigerated { get; set; } = false;
    public int? MaxTrailerLengthFeet { get; set; }
    public string? Notes { get; set; }

    public bool IsOperational => Status == DockDoorStatus.Active;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public ICollection<DockAppointment> Appointments { get; set; } = [];
    public ICollection<DockAssignment> Assignments { get; set; } = [];

    public DockDoor()
    {
        Id = Guid.NewGuid();
    }
}

