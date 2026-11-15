namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of a consolidated carrier shipment master.
/// Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public enum ShipmentStatus
{
    Draft = 1,             // Accumulating packed cartons
    ReadyForDispatch = 2,  // All packages weighed, labeled, and staged
    Dispatched = 3,        // BOL signed and custody transferred to carrier
    Cancelled = 4          // Cancelled prior to carrier handover
}

