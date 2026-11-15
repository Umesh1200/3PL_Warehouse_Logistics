namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Root cause reason for an unfulfilled demand shortage during allocation.
/// Phase 4.2 Allocation Engine.
/// </summary>
public enum ShortageReason
{
    InsufficientStock = 1,     // Zero or inadequate on-hand inventory
    NoValidLotsAvailable = 2,  // All on-hand lots on QA hold or quarantined
    ExpiredLotsOnly = 3,       // Available stock failed expiration cutoff
    LotRestrictionUnmet = 4,   // Order specified lot not present in storage
    LocationLocked = 5,        // Bin under active cycle count or maintenance lock
    TempClassMismatch = 6      // Stock location violates temperature integrity
}

