using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Catalog;

public class ItemCategoryMapping : BaseEntity<long>
{
    public Guid ItemId { get; set; }
    public int CategoryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigations
    public Item Item { get; set; } = null!;
    public ItemCategory Category { get; set; } = null!;
}

