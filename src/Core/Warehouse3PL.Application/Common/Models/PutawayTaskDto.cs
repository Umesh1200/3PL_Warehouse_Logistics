using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class PutawayTaskDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }

    public Guid? LpnId { get; set; }
    public string? LpnCode { get; set; }

    public Guid SourceLocationId { get; set; }
    public string SourceLocationCode { get; set; } = string.Empty;

    public Guid SuggestedLocationId { get; set; }
    public string SuggestedLocationCode { get; set; } = string.Empty;
    public string SuggestedLocationBarcode { get; set; } = string.Empty;

    public Guid TargetLocationId
    {
        get => SuggestedLocationId;
        set => SuggestedLocationId = value;
    }
    public string TargetLocationCode
    {
        get => SuggestedLocationCode;
        set => SuggestedLocationCode = value;
    }

    public Guid? ActualLocationId { get; set; }
    public string? ActualLocationCode { get; set; }

    public decimal Quantity { get; set; }
    public PutawayTaskStatus Status { get; set; }
    public PutawayPriority Priority { get; set; } = PutawayPriority.Normal;

    public Guid? AssignedUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CompletedAt
    {
        get => CompletedAtUtc;
        set => CompletedAtUtc = value;
    }

    public string? ExceptionReason { get; set; }
    public string? Notes { get; set; }
    public uint Version { get; set; }
}
