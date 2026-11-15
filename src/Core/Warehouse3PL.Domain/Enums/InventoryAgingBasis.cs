namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational time basis used to evaluate inventory age and holding duration.
/// Invariant: Receipt age, movement age, demand age, and expiry proximity are independent dimensions.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum InventoryAgingBasis
{
    ReceiptDate = 1,
    LastMovementDate = 2,
    LastDemandDate = 3,
    ExpirationProximity = 4
}

