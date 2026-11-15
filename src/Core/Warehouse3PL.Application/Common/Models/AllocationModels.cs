using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Input parameters requesting candidate inventory evaluation and allocation planning for an order line.
/// </summary>
public class AllocationRequest
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public Guid OutboundOrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public decimal QuantityRequested { get; set; }

    public string? RequiredLotNumber { get; set; }
    public DateTime? MinExpiryDateUtc { get; set; }

    public AllocationPolicy? Policy { get; set; }

    // Manual designation criteria
    public Guid? PreferredLocationId { get; set; }
    public Guid? PreferredLotId { get; set; }
    public string? PreferredLpnCode { get; set; }
}

/// <summary>
/// Evaluation outcome produced by the shared candidate eligibility filter.
/// </summary>
public class CandidateEligibilityResult
{
    public bool IsEligible { get; set; }
    public ShortageReason? IneligibilityReason { get; set; }
    public string? ReasonDetails { get; set; }

    public static CandidateEligibilityResult Success() => new() { IsEligible = true };

    public static CandidateEligibilityResult Ineligible(ShortageReason reason, string details) =>
        new() { IsEligible = false, IneligibilityReason = reason, ReasonDetails = details };
}

/// <summary>
/// Proposed allocation from a specific storage location / bin for an order line.
/// Pure in-memory plan; does NOT mutate inventory balances.
/// </summary>
public class CandidateAllocationPlan
{
    public Guid StockOnHandId { get; set; }
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }
    public string? LpnCode { get; set; }
    public decimal QuantityAllocated { get; set; }
    public decimal AvailableInBinBefore { get; set; }
    public decimal AvailableInBinRemaining { get; set; }
    public int? PickSequence { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
}

/// <summary>
/// Unfulfilled demand deficit identified during allocation planning.
/// </summary>
public class AllocationShortagePlan
{
    public Guid OutboundOrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public decimal ShortageQuantity { get; set; }
    public ShortageReason Reason { get; set; }
    public string Details { get; set; } = string.Empty;
}

/// <summary>
/// Multi-bin allocation plan for a single order line.
/// </summary>
public class LineAllocationPlan
{
    public Guid OutboundOrderLineId { get; set; }
    public int LineNumber { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal QuantityRequested { get; set; }
    public decimal QuantityPlanned { get; set; }
    public decimal ShortageQuantity => Math.Max(0m, QuantityRequested - QuantityPlanned);
    public bool IsFullyPlanned => QuantityPlanned >= QuantityRequested;
    public AllocationStrategyType StrategyUsed { get; set; }
    public List<CandidateAllocationPlan> CandidateAllocations { get; set; } = [];
    public List<AllocationShortagePlan> Shortages { get; set; } = [];
}

/// <summary>
/// Comprehensive allocation plan for an entire outbound order across all active lines.
/// Pure in-memory simulation; guarantees zero inventory mutation.
/// </summary>
public class OrderAllocationPlan
{
    public Guid OutboundOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public decimal TotalQuantityRequested { get; set; }
    public decimal TotalQuantityPlanned { get; set; }
    public decimal TotalShortageQuantity { get; set; }
    public bool IsFullyPlanned => TotalQuantityPlanned >= TotalQuantityRequested;
    public List<LineAllocationPlan> LinePlans { get; set; } = [];
}

