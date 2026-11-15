using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Catalog;

/// <summary>
/// Hierarchical product categories.
/// </summary>
public class ItemCategory : BaseEntity<int>
{
    public int? ParentCategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public bool StorageAttributeFlag { get; set; } = false;

    // Navigations
    public ItemCategory? ParentCategory { get; set; }
    public ICollection<ItemCategory> ChildCategories { get; set; } = [];
    public ICollection<ItemCategoryMapping> CategoryMappings { get; set; } = [];
}

