using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class DockDoorDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public string DoorNumber { get; set; } = string.Empty;
    public DockDoorType DoorType { get; set; }
    public DockDoorStatus Status { get; set; }
    public bool SupportsRefrigerated { get; set; }
    public int? MaxTrailerLengthFeet { get; set; }
    public string? Notes { get; set; }
    public bool IsOperational { get; set; }
    public DateTime CreatedAt { get; set; }
}

