using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.CrossDock;

/// <summary>
/// Specific SKU flow-through requirement within a CrossDockPlan.
/// </summary>
public class CrossDockPlanLine : BaseEntity<Guid>, ISoftDeletable
{
    public Guid CrossDockPlanId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? ReceiptLineId { get; set; }

    public string? InboundLpn { get; set; }

    public Guid SourceLocationId { get; set; } // Receiving Staging
    public Guid TargetLocationId { get; set; } // Outbound Staging / Marshaling

    public decimal InboundAvailableQuantity { get; set; }
    public decimal OutboundDemandQuantity { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ExecutedQuantity { get; set; }

    public CrossDockLineStatus Status { get; set; } = CrossDockLineStatus.Planned;

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public CrossDockPlan CrossDockPlan { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public ReceiptLine? ReceiptLine { get; set; }
    public Location SourceLocation { get; set; } = null!;
    public Location TargetLocation { get; set; } = null!;

    public ICollection<CrossDockAllocation> Allocations { get; set; } = [];

    public CrossDockPlanLine()
    {
        Id = Guid.NewGuid();
    }
}

