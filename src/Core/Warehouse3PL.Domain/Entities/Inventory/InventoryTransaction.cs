using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Immutable Inventory Movement Ledger (DB-SPEC-3PL-2026 Module 6).
/// Every inventory quantity alteration produces an un-updateable append-only ledger entry.
/// </summary>
public class InventoryTransaction : BaseEntity<long>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? LotId { get; set; }
    
    public Guid? FromLocationId { get; set; }
    public Guid? ToLocationId { get; set; }

    public InventoryTxType TxType { get; set; } = InventoryTxType.Move;
    public decimal Quantity { get; set; }
    
    // Audit trace link
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public Guid? UserId { get; set; }
    public string? Notes { get; set; }
    public DateTime TxTimestamp { get; set; } = DateTime.UtcNow;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public Location? FromLocation { get; set; }
    public Location? ToLocation { get; set; }
    public User? User { get; set; }
}

