using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class CrossDockPlanDto
{
    public Guid Id { get; set; }
    public string PlanNumber { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? ReceivingSessionId { get; set; }
    public CrossDockPlanStatus Status { get; set; }

    public decimal TotalPlannedQuantity { get; set; }
    public decimal TotalExecutedQuantity { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public uint Version { get; set; }

    public List<CrossDockPlanLineDto> Lines { get; set; } = [];
    public List<CrossDockTaskDto> Tasks { get; set; } = [];
}

public class CrossDockPlanLineDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;

    public Guid? ReceiptLineId { get; set; }
    public string? InboundLpn { get; set; }

    public Guid SourceLocationId { get; set; }
    public string SourceLocationCode { get; set; } = string.Empty;

    public Guid TargetLocationId { get; set; }
    public string TargetLocationCode { get; set; } = string.Empty;

    public decimal InboundAvailableQuantity { get; set; }
    public decimal OutboundDemandQuantity { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ExecutedQuantity { get; set; }
    public CrossDockLineStatus Status { get; set; }

    public List<CrossDockAllocationDto> Allocations { get; set; } = [];
}

public class CrossDockAllocationDto
{
    public Guid Id { get; set; }
    public Guid OutboundOrderId { get; set; }
    public string OutboundOrderNumber { get; set; } = string.Empty;
    public Guid OutboundOrderLineId { get; set; }

    public decimal AllocatedQuantity { get; set; }
    public CrossDockAllocationStatus Status { get; set; }
    public DateTime AllocatedAtUtc { get; set; }
    public DateTime? TransferredAtUtc { get; set; }
}

