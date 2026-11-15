using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class YardVisitDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public Guid? ClientId { get; set; }
    public Guid? AsnId { get; set; }
    public string? AsnNumber { get; set; }
    public Guid? DockAppointmentId { get; set; }
    public string? AppointmentNumber { get; set; }
    public string VisitNumber { get; set; } = string.Empty;
    public string CarrierName { get; set; } = string.Empty;
    public string TrailerNumber { get; set; } = string.Empty;
    public string? LicensePlate { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public DateTime CheckInTime { get; set; }
    public DateTime? DepartureTime { get; set; }
    public YardVisitStatus Status { get; set; }
    public string? ParkingLocation { get; set; }
    public string? Notes { get; set; }
    public string? CurrentDockDoorNumber { get; set; }
    public uint Version { get; set; }
    public DateTime CreatedAt { get; set; }
}

