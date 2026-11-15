using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.CrossDock;

/// <summary>
/// Hard allocation binding an inbound quantity/LPN to an outbound order line.
/// </summary>
public class CrossDockAllocation : BaseEntity<Guid>, ISoftDeletable
{
    public Guid CrossDockPlanLineId { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? LotId { get; set; }

    public string? InboundLpn { get; set; }

    public Guid OutboundOrderId { get; set; }
    public Guid OutboundOrderLineId { get; set; }

    public decimal AllocatedQuantity { get; set; }
    public CrossDockAllocationStatus Status { get; set; } = CrossDockAllocationStatus.Reserved;

    public DateTime AllocatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? TransferredAtUtc { get; set; }

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public CrossDockPlanLine CrossDockPlanLine { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public OutboundOrder OutboundOrder { get; set; } = null!;
    public OutboundOrderLine OutboundOrderLine { get; set; } = null!;

    public CrossDockAllocation()
    {
        Id = Guid.NewGuid();
    }
}

