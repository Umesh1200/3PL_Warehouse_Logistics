namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Inventory cost valuation basis for Annual Consumption Value (ACV) and financial turnover analytics.
/// Explicitly separates cost-of-goods basis from customer sale prices.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum InventoryValuationBasis
{
    StandardCost = 1,
    UnitCost = 2,
    MovingAverageCost = 3,
    FifoCost = 4,
    CatalogUnitPrice = 5
}

