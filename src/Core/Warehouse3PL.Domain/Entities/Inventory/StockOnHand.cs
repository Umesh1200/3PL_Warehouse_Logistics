using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Real-time inventory balance state snapshot per Tenant, Facility, SKU, Location, and Lot.
/// Concurrency-controlled via optimistic concurrency version.
/// </summary>
public class StockOnHand : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? LotId { get; set; }

    /// <summary>
    /// Optional reference to movable HandlingUnit (Tote / Cart Slot / Pallet LPN).
    /// Enables tracking picked inventory inside a specific handling unit at a packing station or in transit.
    /// </summary>
    public Guid? HandlingUnitId { get; set; }
    public string? LpnCode { get; set; }

    // Granular inventory state partitioning
    public decimal QtyAvailable { get; set; }
    public decimal QtyAllocated { get; set; }
    public decimal QtyReserved { get; set; }
    public decimal QtyQuarantined { get; set; }
    public decimal QtyDamaged { get; set; }
    public decimal QtyOnHold { get; set; }

    public decimal TotalQuantity =>
        QtyAvailable + QtyAllocated + QtyReserved + QtyQuarantined + QtyDamaged + QtyOnHold;

    /// <summary>
    /// Timestamp when inventory was physically received into the warehouse facility.
    /// Primary sorting criterion for FIFO allocation strategy.
    /// </summary>
    public DateTime? ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastCountedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token ensuring zero overselling / race conditions during concurrent allocations.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public HandlingUnit? HandlingUnit { get; set; }

    public StockOnHand()
    {
        Id = Guid.NewGuid();
    }
}

