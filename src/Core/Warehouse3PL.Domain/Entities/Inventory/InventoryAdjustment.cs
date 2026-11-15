using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Inventory Adjustment record documenting approved discrepancy reconciliations.
/// </summary>
public class InventoryAdjustment : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid StockId { get; set; }
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? LotId { get; set; }

    public decimal QtyAdjusted { get; set; } // Positive for gain, negative for shrinkage
    public AdjustmentReasonCode ReasonCode { get; set; } = AdjustmentReasonCode.Miscount;
    public string? ReasonNotes { get; set; }
    public decimal? AccountingImpactAmount { get; set; }

    public Guid? ApprovedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public StockOnHand Stock { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public User? ApprovedByUser { get; set; }

    public InventoryAdjustment()
    {
        Id = Guid.NewGuid();
    }
}

