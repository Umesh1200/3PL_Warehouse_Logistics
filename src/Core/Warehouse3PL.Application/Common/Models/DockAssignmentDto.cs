using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class DockAssignmentDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public Guid DockDoorId { get; set; }
    public string DoorNumber { get; set; } = string.Empty;
    public Guid YardVisitId { get; set; }
    public string VisitNumber { get; set; } = string.Empty;
    public string TrailerNumber { get; set; } = string.Empty;
    public Guid? AsnId { get; set; }
    public string? AsnNumber { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? ArrivedAtDock { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DockAssignmentStatus Status { get; set; }
    public string? Notes { get; set; }
    public uint Version { get; set; }
}

public class ReceivingQueueItemDto
{
    public Guid YardVisitId { get; set; }
    public string VisitNumber { get; set; } = string.Empty;
    public Guid? AsnId { get; set; }
    public string? AsnNumber { get; set; }
    public string? PoNumber { get; set; }
    public string CarrierName { get; set; } = string.Empty;
    public string TrailerNumber { get; set; } = string.Empty;
    public YardVisitStatus Status { get; set; }
    public DateTime CheckInTime { get; set; }
    public double WaitDurationMinutes { get; set; }
    public string? AssignedDockDoorNumber { get; set; }
    public DateTime? ScheduledStartTime { get; set; }
}

