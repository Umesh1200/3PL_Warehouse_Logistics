using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Individual line item within an active cycle count audit.
/// </summary>
public class CycleCountItem : BaseEntity<Guid>
{
    public Guid CycleCountId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? LotId { get; set; }

    public decimal ExpectedQty { get; set; }
    public decimal? CountedQty { get; set; }
    public decimal? VarianceQty => CountedQty.HasValue ? CountedQty.Value - ExpectedQty : null;

    public Guid? CounterUserId { get; set; }
    public DateTime? CountedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IsReconciled { get; set; } = false;

    // Navigations
    public CycleCount CycleCount { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public User? CounterUser { get; set; }
    public User? ApprovedByUser { get; set; }

    public CycleCountItem()
    {
        Id = Guid.NewGuid();
    }
}

