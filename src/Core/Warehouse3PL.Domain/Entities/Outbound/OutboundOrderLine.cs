using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Individual SKU demand requirement within an OutboundOrder.
/// Tracks ordered, allocated, and shipped quantity accounting, plus lot/expiry constraints.
/// Phase 4.1 Order Management.
/// </summary>
public class OutboundOrderLine : BaseEntity<Guid>, ISoftDeletable
{
    public Guid OutboundOrderId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? PackagingUnitId { get; set; }

    public int LineNumber { get; set; }
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityAllocated { get; set; }
    public decimal QuantityShipped { get; set; }

    public decimal RemainingDemand => Math.Max(0, QuantityOrdered - QuantityAllocated);

    public string? RequiredLotNumber { get; set; }
    public DateTime? MinExpiryDateUtc { get; set; }

    public OutboundOrderLineStatus Status { get; set; } = OutboundOrderLineStatus.Pending;
    public string? Notes { get; set; }

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public OutboundOrder OutboundOrder { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public PackagingUnit? PackagingUnit { get; set; }

    public ICollection<InventoryAllocation> Allocations { get; set; } = [];
    public ICollection<AllocationShortage> Shortages { get; set; } = [];

    public OutboundOrderLine()
    {
        Id = Guid.NewGuid();
    }

    public void Allocate(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ValidationException(nameof(quantity), "Allocation quantity must be greater than zero.");
        }

        if (QuantityAllocated + quantity > QuantityOrdered)
        {
            throw new ValidationException(nameof(quantity), $"Cannot allocate {quantity} units. Ordered: {QuantityOrdered}, Already Allocated: {QuantityAllocated}.");
        }

        QuantityAllocated += quantity;
        if (QuantityAllocated >= QuantityOrdered)
        {
            Status = OutboundOrderLineStatus.Allocated;
        }
        else
        {
            Status = OutboundOrderLineStatus.PartiallyAllocated;
        }
    }

    public void Deallocate(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ValidationException(nameof(quantity), "Deallocation quantity must be greater than zero.");
        }

        QuantityAllocated = Math.Max(0, QuantityAllocated - quantity);
        if (QuantityAllocated == 0)
        {
            Status = OutboundOrderLineStatus.Pending;
        }
        else if (QuantityAllocated < QuantityOrdered)
        {
            Status = OutboundOrderLineStatus.PartiallyAllocated;
        }
    }

    public void Cancel()
    {
        Status = OutboundOrderLineStatus.Cancelled;
        QuantityAllocated = 0;
    }
}
