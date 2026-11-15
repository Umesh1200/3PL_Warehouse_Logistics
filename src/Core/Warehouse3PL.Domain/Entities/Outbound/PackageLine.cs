using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Individual line item packed within an outbound shipping carton.
/// Maintains full bidirectional traceability to the source picking tote and original inventory reservation.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class PackageLine : BaseEntity<Guid>
{
    public Guid PackageId { get; set; }
    public Guid OutboundOrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? LotId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>
    /// Pick tote (HandlingUnit) from which inventory was transferred into this carton.
    /// </summary>
    public Guid? SourceHandlingUnitId { get; set; }

    /// <summary>
    /// Direct link to authoritative inventory reservation record.
    /// </summary>
    public Guid InventoryAllocationId { get; set; }

    // Navigations
    public Package Package { get; set; } = null!;
    public OutboundOrderLine OutboundOrderLine { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public HandlingUnit? SourceHandlingUnit { get; set; }
    public InventoryAllocation InventoryAllocation { get; set; } = null!;

    public PackageLine()
    {
        Id = Guid.NewGuid();
    }
}

