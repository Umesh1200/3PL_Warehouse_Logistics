namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational state and condition of warehouse inventory evaluated for commercial storage billing.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public enum InventoryHoldingStatus
{
    /// <summary>
    /// Prime inventory available for allocation and sale.
    /// </summary>
    Available = 1,

    /// <summary>
    /// Inventory committed to an active outbound order or pick wave.
    /// </summary>
    Allocated = 2,

    /// <summary>
    /// Physically picked inventory in totes, carts, or packing station awaiting consolidation.
    /// </summary>
    Picked = 3,

    /// <summary>
    /// Inventory identified as physically damaged during handling or receipt.
    /// </summary>
    Damaged = 4,

    /// <summary>
    /// Inventory placed on QA, recall, or regulatory quarantine hold.
    /// </summary>
    Quarantined = 5,

    /// <summary>
    /// Inventory placed on administrative or billing lock.
    /// </summary>
    OnHold = 6,

    /// <summary>
    /// Inventory currently in-transit between facilities or docks.
    /// </summary>
    InTransit = 7,

    /// <summary>
    /// Reverse-logistics returned inventory awaiting physical inspection and disposition.
    /// </summary>
    ReturnPendingInspection = 8
}

