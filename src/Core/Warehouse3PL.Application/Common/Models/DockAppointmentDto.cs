using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class DockAppointmentDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid AsnId { get; set; }
    public string AsnNumber { get; set; } = string.Empty;
    public Guid? DockDoorId { get; set; }
    public string? DoorNumber { get; set; }
    public string AppointmentNumber { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public DockAppointmentStatus Status { get; set; }
    public string? CarrierName { get; set; }
    public string? TrailerNumber { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public uint Version { get; set; }
    public DateTime CreatedAt { get; set; }
}

