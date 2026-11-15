namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational velocity classification of inventory based on demand frequency, turnover, and pick rates.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum InventoryMovementVelocityCategory
{
    FastMoving = 1,
    MediumMoving = 2,
    SlowMoving = 3,
    NonMoving = 4,
    DeadStock = 5
}

