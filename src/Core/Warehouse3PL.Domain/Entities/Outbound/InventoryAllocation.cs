using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Hard inventory reservation record linking an OutboundOrderLine to a specific StockOnHand record (bin, lot, LPN).
/// Prevents double-allocation without physically moving inventory (TxType.Pick deferred to Phase 4.3).
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public class InventoryAllocation : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public Guid OutboundOrderId { get; set; }
    public Guid OutboundOrderLineId { get; set; }

    public Guid StockOnHandId { get; set; }
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? LotId { get; set; }
    public string? LpnCode { get; set; }

    public decimal QuantityRequested { get; set; }
    public decimal QuantityAllocated { get; set; }
    public decimal QuantityReleased { get; set; }

    /// <summary>
    /// Quantity physically picked into destination handling units (totes/carts).
    /// </summary>
    public decimal QuantityPicked { get; set; } = 0m;

    /// <summary>
    /// Effective active reserved quantity across storage bin and destination handling unit.
    /// </summary>
    public decimal ActiveAllocatedQuantity => Math.Max(0m, QuantityAllocated - QuantityReleased);

    /// <summary>
    /// Remaining allocated quantity waiting to be physically picked from the source bin.
    /// </summary>
    public decimal RemainingToPickQuantity => Math.Max(0m, ActiveAllocatedQuantity - QuantityPicked);

    /// <summary>
    /// Reference to the StockOnHand balance record in the destination tote.
    /// Downstream packing and sorting inspect this record to locate picked stock in the tote.
    /// </summary>
    public Guid? PickedStockOnHandId { get; set; }
    public Guid? TargetHandlingUnitId { get; set; }
    public string? TargetLpnCode { get; set; }

    public AllocationStatus Status { get; set; } = AllocationStatus.Allocated;
    public AllocationStrategyType StrategyUsed { get; set; } = AllocationStrategyType.FIFO;

    public DateTime AllocatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? AllocatedByUserId { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }
    public Guid? ReleasedByUserId { get; set; }
    public string? ReleaseReason { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking on allocation updates and releases.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public OutboundOrder OutboundOrder { get; set; } = null!;
    public OutboundOrderLine OutboundOrderLine { get; set; } = null!;
    public StockOnHand StockOnHand { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public StockOnHand? PickedStockOnHand { get; set; }
    public HandlingUnit? TargetHandlingUnit { get; set; }

    public InventoryAllocation()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Records physical confirmation of picked stock into a destination handling unit / tote,
    /// establishing full traceability from the source bin reservation to the tote inventory balance.
    /// </summary>
    public void RecordPick(decimal quantity, Guid pickedStockId, Guid handlingUnitId, string targetLpnCode)
    {
        if (quantity <= 0)
        {
            throw new ValidationException(nameof(quantity), "Picked quantity must be greater than zero.");
        }

        if (quantity > RemainingToPickQuantity)
        {
            throw new ValidationException(nameof(quantity),
                $"Cannot record pick of {quantity} units. Remaining quantity to pick on allocation '{Id}' is only {RemainingToPickQuantity} units.");
        }

        QuantityPicked += quantity;
        PickedStockOnHandId = pickedStockId;
        TargetHandlingUnitId = handlingUnitId;
        TargetLpnCode = targetLpnCode;

        if (RemainingToPickQuantity == 0)
        {
            Status = AllocationStatus.Picked;
        }
        else
        {
            Status = AllocationStatus.PartiallyPicked;
        }
    }

    /// <summary>
    /// Releases a portion or all of the reserved inventory back to available stock.
    /// Preserves the historical record for audit and reporting.
    /// </summary>
    public void Release(decimal quantityToRelease, Guid userId, string reason)
    {
        if (Status != AllocationStatus.Allocated && Status != AllocationStatus.PartiallyPicked)
        {
            throw new InvalidDomainStateException(
                $"Cannot release allocation '{Id}' with status '{Status}'. Only Allocated or PartiallyPicked allocations can be released.");
        }

        if (quantityToRelease <= 0)
        {
            throw new ValidationException(nameof(quantityToRelease), "Release quantity must be greater than zero.");
        }

        if (quantityToRelease > RemainingToPickQuantity)
        {
            throw new ValidationException(nameof(quantityToRelease),
                $"Cannot release {quantityToRelease} units. Active allocated quantity is only {ActiveAllocatedQuantity} units.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when releasing an allocation.");
        }

        QuantityReleased += quantityToRelease;
        ReleasedAtUtc = DateTime.UtcNow;
        ReleasedByUserId = userId;
        ReleaseReason = reason;

        if (RemainingToPickQuantity == 0)
        {
            Status = QuantityPicked > 0 ? AllocationStatus.Picked : AllocationStatus.Released;
        }
    }

    /// <summary>
    /// Cancels any remaining active reservation on this allocation.
    /// </summary>
    public void Cancel(Guid userId, string reason)
    {
        if (ActiveAllocatedQuantity > 0)
        {
            Release(ActiveAllocatedQuantity, userId, reason);
        }
        Status = AllocationStatus.Cancelled;
    }
}

