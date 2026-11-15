using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Inventory Lot and Batch entity for traceability, expiry, and QA tracking.
/// </summary>
public class InventoryLot : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid ItemId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public LotQaStatus QaStatus { get; set; } = LotQaStatus.Released;
    public string? QaNotes { get; set; }

    public bool IsExpired => ExpirationDate.HasValue && ExpirationDate.Value <= DateTime.UtcNow;

    // Navigations
    public Client Client { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public ICollection<StockOnHand> StockRecords { get; set; } = [];

    public InventoryLot()
    {
        Id = Guid.NewGuid();
    }
}

