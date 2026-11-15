using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class ReceivingSessionDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public Guid? DockDoorId { get; set; }
    public string? DoorNumber { get; set; }
    public Guid? YardVisitId { get; set; }
    public string? VisitNumber { get; set; }
    public string? TrailerNumber { get; set; }
    public Guid? ClientId { get; set; }
    public string? ClientName { get; set; }
    public Guid? AsnId { get; set; }
    public string? AsnNumber { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string? PoNumber { get; set; }
    public string SessionNumber { get; set; } = string.Empty;
    public ReceivingStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid ReceivedByUserId { get; set; }
    public string? ReceivedByUserName { get; set; }
    public decimal? InitialProbeTemperature { get; set; }
    public string? SealNumber { get; set; }
    public bool? SealIntact { get; set; }
    public string? Notes { get; set; }
    public bool IsBlindReceiving { get; set; }
    public bool RequiresSupervisorApproval { get; set; }
    public bool IsSupervisorApproved { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public uint Version { get; set; }
    public List<ReceiptLineDto> Lines { get; set; } = [];
    public List<ReceivingDiscrepancyDto> Discrepancies { get; set; } = [];
}
