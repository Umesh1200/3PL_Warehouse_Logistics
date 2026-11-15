namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Execution mode for a pick wave.
/// Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public enum PickWaveType
{
    DiscreteOrder = 1,  // Single order picked directly into shipping container
    BatchPick = 2,      // Multiple orders picked together into multi-tote cart
    ZonePick = 3,       // Split by warehouse zone, consolidated at packing station
    BulkPick = 4        // Full pallet / case handling for B2B or stock transfer
}

