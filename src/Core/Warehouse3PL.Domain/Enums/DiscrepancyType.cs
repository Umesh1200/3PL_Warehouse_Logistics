namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Classification of receiving discrepancies captured during goods intake or quality inspection.
/// </summary>
public enum DiscrepancyType
{
    QuantityOverage = 1,
    QuantityShortage = 2,
    DamagedGoods = 3,
    TempExcursion = 4,
    UnexpectedSKU = 5
}

