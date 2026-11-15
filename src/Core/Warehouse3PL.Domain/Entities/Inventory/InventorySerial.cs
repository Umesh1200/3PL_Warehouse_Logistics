using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Serial number tracking for individual serialized units.
/// </summary>
public class InventorySerial : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? LotId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public SerialStatus Status { get; set; } = SerialStatus.InStock;

    // Navigations
    public Client Client { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public InventoryLot? Lot { get; set; }

    public InventorySerial()
    {
        Id = Guid.NewGuid();
    }
}

