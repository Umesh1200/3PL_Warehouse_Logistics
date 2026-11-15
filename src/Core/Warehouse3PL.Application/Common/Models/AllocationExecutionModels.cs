using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Output summary of a completed or idempotent order allocation execution.
/// </summary>
public class AllocationExecutionResultDto
{
    public Guid OutboundOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OutboundOrderStatus Status { get; set; }
    public decimal TotalQuantityOrdered { get; set; }
    public decimal TotalQuantityAllocated { get; set; }
    public decimal TotalShortageQuantity { get; set; }
    public bool IsFullyAllocated => TotalQuantityAllocated >= TotalQuantityOrdered;
    public bool IsDuplicateRequest { get; set; }
    public int AttemptCount { get; set; }
    public List<LineAllocationSummaryDto> LineSummaries { get; set; } = [];
    public List<AllocationShortageDto> Shortages { get; set; } = [];
}

public class LineAllocationSummaryDto
{
    public Guid LineId { get; set; }
    public int LineNumber { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityAllocated { get; set; }
    public decimal RemainingDemand { get; set; }
    public OutboundOrderLineStatus Status { get; set; }
    public List<AllocationItemDto> Allocations { get; set; } = [];
}

public class AllocationItemDto
{
    public Guid AllocationId { get; set; }
    public Guid StockOnHandId { get; set; }
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }
    public string? LpnCode { get; set; }
    public decimal QuantityAllocated { get; set; }
    public AllocationStatus Status { get; set; }
}

public class AllocationShortageDto
{
    public Guid ShortageId { get; set; }
    public Guid LineId { get; set; }
    public Guid ItemId { get; set; }
    public decimal ShortageQuantity { get; set; }
    public ShortageReason Reason { get; set; }
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// Output summary of a completed de-allocation.
/// </summary>
public class DeallocationExecutionResultDto
{
    public Guid OutboundOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OutboundOrderStatus RestoredStatus { get; set; }
    public decimal TotalQuantityDeallocated { get; set; }
    public int AllocationsReleasedCount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Output summary of an individual allocation release.
/// </summary>
public class ReleaseAllocationResultDto
{
    public Guid AllocationId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public Guid LineId { get; set; }
    public decimal QuantityReleased { get; set; }
    public decimal ActiveRemainingQuantity { get; set; }
    public AllocationStatus AllocationStatus { get; set; }
    public OutboundOrderStatus OrderStatus { get; set; }
    public OutboundOrderLineStatus LineStatus { get; set; }
}

